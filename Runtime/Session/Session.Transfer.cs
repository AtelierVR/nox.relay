using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Nox.Relay.Core.Types.Quit;
using Nox.Relay.Core.Types.Transfer;
using Nox.Sessions;
using UnityEngine.Events;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Relay.Runtime {
	/// <summary>
	/// Server-initiated redirection, in two flavours:
	/// <list type="bullet">
	/// <item><b>transfer</b>: the relay asks the client to move to another relay while keeping its
	/// identity (world, room, user). The client follows an explicit destination and presents an
	/// opaque, client-unique transfer token in its next handshake.</item>
	/// <item><b>move</b>: the relay asks the client to switch room on the same relay. The transport
	/// is kept and only the room membership is rebuilt.</item>
	/// </list>
	/// In both cases the world stays loaded: only the transport (transfer only), the adapter and the
	/// room membership are rebuilt.
	/// </summary>
	public sealed partial class Session {
		/// <summary>Invoked when the relay asks this client to move to another relay or room.</summary>
		public UnityEvent<TransferEvent> OnTransfer { get; } = new();

		/// <summary>Whether a transfer (or room move) is currently in progress.</summary>
		public bool IsTransferring
			=> _transferring;

		private bool _transferring;

		/// <summary>
		/// Entry point of a relay-issued redirection. One that is already running wins: the second
		/// request can only target a relay/room we are not connected to yet.
		/// </summary>
		internal void OnTransferHandler(TransferEvent @event) {
			if (_disposed || _transferring || @event == null)
				return;

			if (!AutoReconnect) {
				Logger.LogWarning(
					$"Transfer to {@event.Address} ignored: automatic reconnection is disabled",
					tag: Tag);
				return;
			}

			TransferAsync(@event).Forget();
		}

		private async UniTask TransferAsync(TransferEvent @event) {
			_transferring = true;

			try {
				await UniTask.SwitchToMainThread();

				// A pending reconnection loop would race with the transfer for the adapter.
				StopReconnect();

				Logger.Log(
					@event.IsRelayChange
						? $"Transferring to {@event.Address} ({@event.Reason})"
						: $"Moving to room {@event.Room} ({@event.Reason})",
					tag: Tag);
				UpdateState(
					Status.Pending,
					@event.IsRelayChange
						? $"Moving to {@event.Address}..."
						: $"Moving to room {@event.Room}...",
					0f,
					cancelable: true);
				NotifyTransfer(@event);

				using var cts = CancellationTokenSource.CreateLinkedTokenSource(
					ConnectCts?.Token ?? CancellationToken.None);

				var succeeded = @event.IsRelayChange
					? await TransferAsync(@event, cts)
					: await MoveAsync(@event, cts);

				// The new connection may already be gone: treat it as a failure instead of
				// reporting a transfer that no longer holds.
				if (succeeded && IsConnected) {
					Logger.Log(
						@event.IsRelayChange
							? $"Transferred to {@event.Address}"
							: $"Moved to room {@event.Room}",
						tag: Tag);

					// Publish the final state before the callbacks so they observe it.
					_transferring = false;
					UpdateState(Status.Ready, "Ready", 1f);
					OnConnectedHandler(true);
					return;
				}

				Logger.LogWarning(
					@event.IsRelayChange
						? $"Transfer to {@event.Address} failed"
						: $"Move to room {@event.Room} failed",
					tag: Tag);
			} catch (OperationCanceledException) {
				// Disposed or cancelled mid-transfer: nothing to report.
				return;
			} catch (Exception e) {
				Logger.LogException(e, tag: Tag);
			} finally {
				_transferring = false;
			}

			if (IsDisposed)
				return;

			// The token is tied to this client and the room is gone: retrying is pointless, rebuild
			// the room on the relay we can reach so the session never stays empty.
			UpdateState(Status.Pending, "Transfer failed, reconnecting...", 0f, cancelable: true);
			StartReconnect("transfer failed");
		}

		/// <summary>
		/// Changes relay: drop everything owned by the connection we are leaving, release the
		/// transport, then connect to the target with the transfer token.
		/// </summary>
		private async UniTask<bool> TransferAsync(
			TransferEvent @event,
			CancellationTokenSource cts
		) {
			PrepareForReconnect();
			await DetachAdapterAsync();

			if (IsDisposed || cts.IsCancellationRequested)
				return false;

			return await Helper.Reconnect(
				this,
				cts.Token,
				addressOverride: @event.Address,
				transferToken: @event.Token
			);
		}

		/// <summary>
		/// Switches room on the current relay: the relay only drops the player on a quit, then the
		/// room membership is rebuilt on the same adapter.
		/// </summary>
		private async UniTask<bool> MoveAsync(TransferEvent @event, CancellationTokenSource cts) {
			if (@event.Room == byte.MaxValue) {
				Logger.LogWarning($"Move ignored: invalid room {@event.Room}", tag: Tag);
				return false;
			}

			if (Room != null) {
				try {
					await Room.Quit(QuitType.Normal, @event.Reason);
				} catch (Exception e) {
					Logger.LogException(e, tag: Tag);
				}
			}

			PrepareForReconnect();

			if (IsDisposed || cts.IsCancellationRequested)
				return false;

			return await Helper.Move(this, @event.Room, cts.Token);
		}

		/// <summary>Notifies listeners of a relay-issued redirection.</summary>
		private void NotifyTransfer(TransferEvent @event) {
			try {
				OnTransfer.Invoke(@event);
			} catch (Exception e) {
				// A faulty listener must never abort the transfer.
				Logger.LogException(e, tag: Tag);
			}
		}
	}
}
