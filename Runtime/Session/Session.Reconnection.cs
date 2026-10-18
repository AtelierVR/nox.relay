using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Nox.Entities;
using Nox.Sessions;
using UnityEngine.Events;
using Logger = Nox.CCK.Utils.Logger;
using Player = Nox.Relay.Runtime.Players.Player;

namespace Nox.Relay.Runtime {
	/// <summary>
	/// Automatic reconnection after an unexpected transport failure.
	/// <para>
	/// The session keeps its identity (world, instance, credentials) across a reconnect: only the
	/// transport, the relay adapter and the room membership are rebuilt. The world itself stays
	/// loaded, so a reconnect never re-downloads or reloads the destination bundle.
	/// </para>
	/// </summary>
	public sealed partial class Session {
		/// <summary>Reconnect automatically after an unexpected disconnection.</summary>
		public bool AutoReconnect { get; set; } = true;

		/// <summary>Maximum number of reconnection attempts; 0 means unlimited.</summary>
		public int ReconnectMaxAttempts { get; set; }

		/// <summary>Delay in seconds before the first retry; doubles after each failure.</summary>
		public float ReconnectInitialDelay { get; set; } = 1f;

		/// <summary>Upper bound in seconds of the reconnect backoff delay.</summary>
		public float ReconnectMaxDelay { get; set; } = 30f;

		/// <summary>Invoked with the attempt number (starting at 1) each time a reconnect is attempted.</summary>
		public UnityEvent<int> OnReconnecting { get; } = new();

		/// <summary>Invoked once the session is connected and back in its room.</summary>
		public UnityEvent OnReconnected { get; } = new();

		private CancellationTokenSource _reconnectCts;
		private bool _reconnecting;
		private int _reconnectGeneration;
		private int _reconnectAttempt;

		/// <summary>Whether a reconnection loop is currently running.</summary>
		public bool IsReconnecting
			=> _reconnecting;

		/// <summary>Number of the current reconnection attempt, or 0 when no reconnection is running.</summary>
		public int ReconnectAttempt
			=> _reconnectAttempt;

		/// <summary>Whether the session has been disposed.</summary>
		internal bool IsDisposed
			=> _disposed;

		/// <summary>
		/// Starts the reconnection loop after a disconnect. Safe to call from any thread: the loop
		/// switches to the main thread before touching any session state. Does nothing when the
		/// session is disposed, when automatic reconnection is disabled, or when a loop is already
		/// running.
		/// </summary>
		internal void StartReconnect(string reason) {
			if (_disposed || !AutoReconnect || _reconnecting)
				return;

			_reconnecting = true;
			var generation = ++_reconnectGeneration;
			_reconnectCts?.Dispose();
			_reconnectCts = new CancellationTokenSource();
			ReconnectLoop(reason, generation, _reconnectCts.Token).Forget();
		}

		/// <summary>Cancels the reconnection loop, if any.</summary>
		private void StopReconnect() {
			_reconnectCts?.Cancel();
			_reconnectCts?.Dispose();
			_reconnectCts = null;
			_reconnecting = false;
			_reconnectAttempt = 0;
		}

		private async UniTask ReconnectLoop(string reason, int generation, CancellationToken token) {
			try {
				await UniTask.SwitchToMainThread();

				// Drop everything the dead connection owned: the relay assigns new ids on the next
				// enter, so the old players/entities can never be reused.
				PrepareForReconnect();

				var attempt = 0;
				var delay   = Math.Max(0.1f, ReconnectInitialDelay);

				while (!token.IsCancellationRequested && !_disposed) {
					attempt++;
					_reconnectAttempt = attempt;
					NotifyReconnecting(attempt);
					UpdateState(
						Status.Pending,
						$"Connection lost ({reason}). Reconnecting... (attempt {attempt})",
						0f,
						cancelable: true
					);

					try {
						if (await Helper.Reconnect(this, token)) {
							// The new connection may already be gone (the server dropped it right away):
							// treat it as a failed attempt instead of reporting a success that no longer holds.
							if (IsConnected) {
								Logger.Log($"Reconnected to the relay (attempt {attempt})", tag: Tag);

								// Publish the final state before the callbacks so they observe it (and a
								// disconnect raised by a callback can start a fresh loop right away).
								_reconnecting = false;
								_reconnectAttempt = 0;

								UpdateState(Status.Ready, "Ready", 1f);
								OnConnectedHandler(true);
								NotifyReconnected();

								return;
							}

							Logger.LogWarning("Reconnected, but the new connection dropped immediately", tag: Tag);
						}
					} catch (OperationCanceledException) {
						throw;
					} catch (Exception e) {
						Logger.LogException(e, tag: Tag);
					}

					if (ReconnectMaxAttempts > 0 && attempt >= ReconnectMaxAttempts) {
						Logger.LogError($"Failed to reconnect after {attempt} attempt(s)", tag: Tag);
						UpdateState(Status.Error, $"Failed to reconnect after {attempt} attempt(s)", -1f);
						return;
					}

					if (delay > 0f) {
						UpdateState(
							Status.Pending,
							$"Connection lost ({reason}). Retrying in {delay:F0}s...",
							0f,
							cancelable: true
						);
						await UniTask.Delay(TimeSpan.FromSeconds(delay), cancellationToken: token);
					}

					delay = Math.Min(delay * 2f, Math.Max(delay, ReconnectMaxDelay));
				}
			} catch (OperationCanceledException) {
				// Disposed or cancelled while reconnecting: nothing to report.
			} catch (Exception e) {
				Logger.LogException(e, tag: Tag);
			} finally {
				await UniTask.SwitchToMainThread();

				// Only the most recent loop owns the flag: a newer loop may already have started.
				if (generation == _reconnectGeneration) {
					_reconnecting = false;
					_reconnectAttempt = 0;
					_reconnectCts?.Dispose();
					_reconnectCts = null;
				}
			}
		}

		/// <summary>Notifies every session module that the client is connected to the relay.</summary>
		private void NotifyModulesConnected() {
			foreach (var module in GetModules())
				TryNotifyConnected(module);
		}

		/// <summary>
		/// Replays the connection state to a module that was loaded after the connection was
		/// established (a world scene streamed in later, a module created on the fly, …).
		/// </summary>
		internal void TryNotifyConnected(ISessionModule module) {
			if (module == null)
				return;

			try {
				module.OnConnected();
			} catch (Exception e) {
				// A faulty module must never break the session.
				Logger.LogException(e, tag: Tag);
			}
		}

		/// <summary>Notifies every session module that the connection was lost.</summary>
		private void NotifyModulesDisconnected(string reason) {
			foreach (var module in GetModules()) {
				try {
					module.OnDisconnected(reason);
				} catch (Exception e) {
					// A faulty module must never break the session.
					Logger.LogException(e, tag: Tag);
				}
			}
		}

		/// <summary>Notifies the public event and every session module of a new attempt.</summary>
		private void NotifyReconnecting(int attempt) {			try {
				OnReconnecting.Invoke(attempt);
			} catch (Exception e) {
				Logger.LogException(e, tag: Tag);
			}

			foreach (var module in GetModules()) {
				try {
					module.OnReconnecting(attempt);
				} catch (Exception e) {
					// A faulty script must never abort the reconnection loop.
					Logger.LogException(e, tag: Tag);
				}
			}
		}

		/// <summary>Notifies the public event and every session module that the session is back.</summary>
		private void NotifyReconnected() {
			try {
				OnReconnected.Invoke();
			} catch (Exception e) {
				Logger.LogException(e, tag: Tag);
			}

			foreach (var module in GetModules()) {
				try {
					module.OnReconnected();
				} catch (Exception e) {
					// A faulty script must never abort the reconnection loop.
					Logger.LogException(e, tag: Tag);
				}
			}
		}

		/// <summary>
		/// Detaches the dead room and disposes the relay-owned entities. Local-only entities (spawned
		/// by a mod on this client) are kept: they are not replicated and therefore still valid.
		/// </summary>
		private void PrepareForReconnect() {
			// Voice routing is bound to the room's stream event: drop it while the room is still set.
			DisposeVoiceRouting();

			// The previous room belongs to the dead connection: unsubscribe before dropping it so a
			// late event can never reach a session that already switched adapters.
			if (Room != null) {
				Room.OnQuited.RemoveListener(OnPlayerQuitedHandler);
				Room.OnJoined.RemoveListener(OnPlayerJoinedHandler);
				Room.OnLeft.RemoveListener(OnPlayerLeftHandler);
				Room.OnTransform.RemoveListener(OnTransformHandler);
				Room.OnProperties.RemoveListener(OnPropertiesHandler);
				Room.OnEvent.RemoveListener(OnEventHandler);
				Room.OnAvatarChanged.RemoveListener(OnAvatarChanged);
				Room.OnEntity.RemoveListener(OnEntityHandler);
				Room.OnEntered.RemoveListener(OnPlayerEnteredHandler);
				Room.OnTraveling.RemoveListener(OnTraveling);
				Room.OnServerConfig.RemoveListener(OnRoomConfigUpdated);
				Room.Players.Clear();
				Room = null;
			}

			_roomInfoHooked = false;

			// Players and relay entities carry ids owned by the dead connection: they will be
			// rebuilt from the join/entity events of the new one. Remote players are announced as
			// leaving (modules/UI drop their references); the local player is just disposed.
			foreach (var player in _entities.GetEntities<Player>()) {
				if (player.IsLocal)
					player.Dispose();
				else
					OnPlayerLeftOrQuitedHandler(player);
			}

			foreach (var entity in _entities.GetEntities<Entity>())
				if (entity is not Player && entity.IsNetworked)
					entity.Dispose();

			_entities.LocalId = Runtime.Entities.InvalidEntityId;
			_entities.MasterId = Runtime.Entities.InvalidEntityId;

			_lastKnownTps = 0;
			OnTickRateChanged.Invoke(0);
			foreach (var module in GetModules())
				module.OnTickRateChanged(0);
		}
	}
}
