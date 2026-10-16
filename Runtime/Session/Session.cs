using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Nox.CCK.Properties;
using Nox.CCK.Sessions;
using Nox.Entities;
using Nox.Relay.Core.Rooms;
using Nox.Relay.Core.Types.ServerConfig;
using Nox.Sessions;
using UnityEngine.Events;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Relay.Runtime {
	public sealed partial class Session : BaseEditablePropertyObject, INetSession, ITeamSession, IPhysicalSession {
		internal Session(string id) {
			Id         = id;
			_state = new State(Status.Pending, "Session is initializing", 0f, cancelable: true);
			SetupDimensions();
			SetupEntities();
		}

		internal IState _state;

		internal Core.Relay Adapter;
		internal Room Room;

		/// <summary>Session-scoped key-value data container.</summary>
		private readonly DataContainer _data = new();

		public IDataContainer Data
			=> _data;

		/// <summary>
		/// Cancels the in-flight connection attempt when the session is disposed or cancelled.
		/// Set by <see cref="Helper.Create"/> before the connect task is started.
		/// </summary>
		internal CancellationTokenSource ConnectCts;

		private bool _disposed;

		/// <summary>The currently active relay session (set on connect, cleared on dispose).</summary>
		internal static Session Current { get; private set; }

		public UnityEvent<IState> OnStateChanged { get; } = new();

		public UnityEvent<int> OnTickRateChanged { get; } = new();

		public string Id { get; }

		public IState State {
			get => _state;
			private set {
				_state = value;
				OnStateChanged.Invoke(value);
				Main.CoreAPI.EventAPI.Emit("session_state_changed", this, value);
			}
		}

		internal void UpdateState(Status stt, string msg, float pg, bool cancelable = false)
			=> UniTask.Post(() => State = new State(stt, msg, pg, cancelable));

		private bool _roomInfoHooked;

		/// <summary>Publishes the room-owned session info (capacity) and signals it with <c>session_updated</c>.</summary>
		internal void PublishRoomInfo() {
			if (Room == null)
				return;

			// Extension method on ISession: the receiver has to be explicit here.
			this.SetCapacity(Room.MaxPlayerCount);
			Main.CoreAPI.EventAPI.Emit("session_updated", this);
		}

		/// <summary>Follows the room information (idempotent: a session can enter several rooms).</summary>
		internal void HookRoomInfo() {
			if (Room == null)
				return;

			if (!_roomInfoHooked) {
				_roomInfoHooked = true;
				Room.OnServerConfig.AddListener(OnRoomConfigUpdated);
			}

			PublishRoomInfo();
		}

		private void OnRoomConfigUpdated(ServerConfigResponse response)
			=> PublishRoomInfo();

		internal string Tag
			=> GetType().Name + $"_{Id}";

		public async UniTask Dispose() {
			if (_disposed)
				return;
			_disposed = true;

			Logger.LogDebug("Disposing session", tag: Tag);

			// Abort any in-flight connection attempt.
			ConnectCts?.Cancel();
			ConnectCts?.Dispose();
			ConnectCts = null;

			Current = null;

			DisposeVoiceRouting();

			if (Adapter != null) {
				Adapter.Connector.OnDisconnected.RemoveListener(OnDisconnectedHandler);
				Adapter.Connector.OnConnected.RemoveListener(OnConnectedHandler);
				Adapter.OnLatency.RemoveListener(OnLatencyUpdated);
				await Adapter.Dispose();
				Adapter = null;
			}

			await UniTask.SwitchToMainThread();

			if (_roomInfoHooked) {
				_roomInfoHooked = false;
				Room?.OnServerConfig.RemoveListener(OnRoomConfigUpdated);
			}

			_data.Dispose();
			_entities?.Dispose();
			_dimensions?.Dispose();
		}

		private DateTime _lastTick = DateTime.MinValue;
		private long _tickCounter;
		private int _lastKnownTps;

		public void Update() {
			var entities = _entities.GetEntities<Entity>();

			foreach (var player in entities)
				player.Update();

			var tps = Room?.Tps ?? 0;
			if (tps != _lastKnownTps) {
				_lastKnownTps = tps;
				OnTickRateChanged.Invoke(tps);
				foreach (var module in GetModules())
					module.OnTickRateChanged(tps);
			}

			if (tps <= 0)
				return;

			var now   = DateTime.UtcNow;
			var delta = (now - _lastTick).TotalSeconds;
			if (!(delta >= 1.0 / tps))
				return;
			_lastTick = now;

			foreach (var player in entities)
				player.Tick();

			var tick = ++_tickCounter;
			foreach (var module in GetModules())
				module.OnTick(tick);
		}

		public int TickRate
			=> Room?.Tps ?? 0;
	}
}