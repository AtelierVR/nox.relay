using System;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Nox.CCK.Properties;
using Nox.CCK.Sessions;
using Nox.CCK.Utils;
using Nox.Entities;
using Nox.Players;
using Nox.Relay.Core.Rooms;
using Nox.Relay.Core.Types.ServerConfig;
using Nox.Sessions;
using Nox.Worlds;
using UnityEngine.Events;
using Logger = Nox.CCK.Utils.Logger;
using Player = Nox.Relay.Runtime.Players.Player;

namespace Nox.Relay.Runtime {
	public sealed partial class Session : BaseEditablePropertyObject, INetSession, ITeamSession, IPhysicalSession {
		internal Session(string id) {
			Id            = id;
			InterEntities = new Entities(this);
			InterState    = new State(Status.Pending, "Session is initializing", 0f, cancelable: true);
		}

		internal IState InterState;
		internal Dimensions InterDimensions;

		/// <summary>
		/// World identifier requested by the current travel, kept until the destination scene
		/// (and therefore the dimensions) is available. <see cref="Identifier.Invalid"/> for a
		/// travel by URL, which has no world identity.
		/// </summary>
		private Identifier _targetIdentifier = Identifier.Invalid;

		readonly internal Entities InterEntities;
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

		/// <summary>
		/// Whether this session targets the given world. While the destination world is still
		/// loading there is no dimension yet, so the requested identifier is used instead: that
		/// is what lets the UI show the progress (and offer to cancel) of a pending session.
		/// </summary>
		public bool Match(Identifier identifier) {
			var current = InterDimensions != null ? InterDimensions.Identifier : _targetIdentifier;
			return current.IsValid() && current.Equals(identifier);
		}

		public string Id { get; }

		public IDimensions Dimensions
			=> InterDimensions;

		public IEntities Entities
			=> InterEntities;

		public IState State {
			get => InterState;
			private set {
				InterState = value;
				OnStateChanged.Invoke(value);
				Main.CoreAPI.EventAPI.Emit("session_state_changed", this, value);
			}
		}

		internal void UpdateState(Status stt, string msg, float pg, bool cancelable = false)
			=> UniTask.Post(() => State = new State(stt, msg, pg, cancelable));

		private void SetDimension(IRuntimeWorld scene) {
			InterDimensions?.Dispose();
			InterDimensions = new Dimensions(this, scene);
			_targetIdentifier = scene.Identifier;
		}

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

		public IPlayer MasterPlayer {
			get => InterEntities.GetEntity<Player>(InterEntities.MasterId);
			set => Logger.LogWarning("Setting the master player is not supported in relay sessions.", tag: Tag);
		}

		public IPlayer LocalPlayer {
			get => InterEntities.GetEntity<Player>(InterEntities.LocalId);
			set => Logger.LogWarning("Setting the local player is not supported in relay sessions.", tag: Tag);
		}

		internal string Tag
			=> GetType().Name + $"_{Id}";

		private ISessionModule[] GetAllModules()
			=> InterDimensions?.GetDescriptors()
				.Where(e => e != null)
				.SelectMany(d => d.GetModules<ISessionModule>())
				.ToArray() ?? Array.Empty<ISessionModule>();


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
			InterEntities?.Dispose();
			InterDimensions?.Dispose();
		}

		private DateTime _lastTick = DateTime.MinValue;
		private long _tickCounter;
		private int _lastKnownTps;

		public void Update() {
			var entities = InterEntities.GetEntities<Entity>();

			foreach (var player in entities)
				player.Update();

			var tps = Room?.Tps ?? 0;
			if (tps != _lastKnownTps) {
				_lastKnownTps = tps;
				OnTickRateChanged.Invoke(tps);
				foreach (var module in GetAllModules())
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
			foreach (var module in GetAllModules())
				module.OnTick(tick);
		}

		public int TickRate
			=> Room?.Tps ?? 0;
	}
}