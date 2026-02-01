using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using Nox.CCK.Properties;
using Nox.CCK.Sessions;
using Nox.CCK.Utils;
using Nox.CCK.Worlds;
using Nox.Controllers;
using Nox.Entities;
using Nox.Players;
using Nox.Relay.Core.Players;
using Nox.Relay.Core.Rooms;
using Nox.Relay.Core.Types.Avatars;
using Nox.Relay.Core.Types.Enter;
using Nox.Relay.Core.Types.Event;
using Nox.Relay.Core.Types.Join;
using Nox.Relay.Core.Types.Leave;
using Nox.Relay.Core.Types.Properties;
using Nox.Relay.Core.Types.Quit;
using Nox.Relay.Core.Types.Transform;
using Nox.Relay.Core.Types.Traveling;
using Nox.Relay.Runtime.Players;
using Nox.Sessions;
using Nox.Worlds;
using UnityEngine;
using UnityEngine.Events;
using Logger = Nox.CCK.Utils.Logger;
using Object = UnityEngine.Object;
using Player = Nox.Relay.Runtime.Players.Player;

namespace Nox.Relay.Runtime {
	public sealed class Session : BaseEditablePropertyObject, INetSession {
		internal Session(string id) {
			Id = id;
			InterEntities = new Entities(this);
			InterState = new State(Status.Pending, "Session is initializing", 0f);
		}

		internal IState InterState;
		internal Dimensions InterDimensions;
		internal readonly Entities InterEntities;
		internal Core.Relay Adapter;
		internal Room Room;

		public UnityEvent OnConnected { get; } = new();
		public UnityEvent<string> OnDisconnected { get; } = new();
		public UnityEvent<IPlayer> OnPlayerJoined { get; } = new();
		public UnityEvent<IPlayer> OnPlayerLeft { get; } = new();
		public UnityEvent<IPlayer, IPlayer> OnAuthorityTransferred { get; } = new();

		public UnityEvent<IPlayer, bool> OnPlayerVisibility { get; } = new();
		public UnityEvent<IEntity> OnEntityRegistered { get; } = new();
		public UnityEvent<IEntity> OnEntityUnregistered { get; } = new();
		public UnityEvent<IState> OnStateChanged { get; } = new();

		public bool Match(IWorldIdentifier identifier)
			=> InterDimensions.Identifier != null && InterDimensions.Identifier.Equals(identifier);


		public UnityEvent<long, byte[], IPlayer> OnEventReceived { get; } = new();

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
			}
		}

		internal void UpdateState(Status stt, string msg, float pg)
			=> State = new State(stt, msg, pg);

		private void SetDimension(IRuntimeWorld scene) {
			InterDimensions?.Dispose();
			InterDimensions = new Dimensions(this, scene);
		}

		internal void SetAdapter(Core.Relay adapter) {
			Adapter?.Connector.OnDisconnected.RemoveListener(OnDisconnectedHandler);
			Adapter?.Connector.OnConnected.RemoveListener(OnConnectedHandler);
			Adapter = adapter;
			Adapter.Connector.OnDisconnected.AddListener(OnDisconnectedHandler);
			Adapter.Connector.OnConnected.AddListener(OnConnectedHandler);
			if (Adapter.Connector.IsConnected)
				OnConnectedHandler();
		}

		private void OnDisconnectedHandler(string reason)
			=> OnDisconnected.Invoke(reason);

		private void OnConnectedHandler()
			=> OnConnected.Invoke();

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
			await UniTask.Yield();
			InterEntities.Dispose();
			InterDimensions.Dispose();
		}

		private DateTime _lastTick = DateTime.MinValue;

		public void Update() {
			var entities = InterEntities.GetEntities<Entity>();

			foreach (var player in entities)
				player.Update();

			var now = DateTime.UtcNow;
			var delta = (now - _lastTick).TotalSeconds;
			if (!(delta >= 1.0 / Room.Tps)) return;
			_lastTick = now;
			
			foreach (var player in entities)
				player.Tick();
		}

		public async UniTask OnSelect(ISession old) {
			Logger.LogDebug("Selecting session", tag: Tag);

			if (InterDimensions == null) {
				Logger.LogDebug("No dimension found", tag: Tag);
				return;
			}

			if (!await InterDimensions.CreateIfMissing(Runtime.Dimensions.MainIndex)) {
				Logger.LogError("Failed to create main scene", tag: Tag);
				return;
			}

			InterDimensions.SetActive(Runtime.Dimensions.MainIndex, true);
			InterDimensions.SetCurrent();

			OnControllerChanged(Main.ControllerAPI.Current);

			foreach (var module in GetAllModules())
				module.OnSessionSelected();
		}




		public void OnSceneLoaded(int index, IWorldDescriptor descriptor, GameObject anchor) {
			Main.CoreAPI.EventAPI.Emit("session_scene_added", this, index, descriptor, anchor);

			var modules = descriptor.GetModules<ISessionModule>();
			Logger.LogDebug($"OnDescriptorAdded: {descriptor} with {modules.Length} modules", descriptor as Object, Tag);

			foreach (var module in modules)
				module.OnLoaded(this);

			foreach (var player in InterEntities.GetEntities<IPlayer>()) {
				if (player == null) continue;
				foreach (var module in modules)
					module.OnPlayerJoined(player);
			}

			var master = MasterPlayer;
			if (master != null)
				foreach (var module in modules)
					module.OnAuthorityTransferred(master, null);

			if (SessionHelper.IsCurrent(Main.SessionAPI, this))
				foreach (var module in modules)
					module.OnSessionSelected();

			foreach (var module in GetAllModules())
				module.OnSceneLoaded(descriptor, index, anchor);
		}

		public void OnSceneUnloaded(int index) {
			Main.CoreAPI.EventAPI.Emit("session_scene_removed", this, index);

			foreach (var module in GetAllModules())
				module.OnSceneUnloaded(index);
		}

		public void OnControllerChanged(IController controller)
			=> InterEntities.LocalPlayer?.UpdateController(controller);

		public async UniTask OnDeselect(ISession @new) {
			Logger.LogDebug("Deselecting session", tag: Tag);

			if (InterDimensions == null) {
				Logger.LogWarning("The scene has no dimension assigned. Skipping visibility updates.", tag: Tag);
				await UniTask.Yield();
				return;
			}

			foreach (var module in GetAllModules())
				module.OnSessionDeselected();

			InterDimensions.SetActive(Runtime.Dimensions.MainIndex, false);

			await UniTask.Yield();

			InterEntities.LocalPlayer?.RemoveController();
		}

		public bool IsConnected
			=> Adapter?.Connector.IsConnected ?? false;

		public DateTime Time
			=> Adapter?.Time ?? DateTime.MinValue;

		public double Ping
			=> Adapter?.Ping ?? -1;


		public UniTask<bool> EmitEvent(long @event, byte[] raw)
			=> Room.Event(EventRequest.Broadcast(@event, raw));

		internal void OnPlayerQuitedHandler(QuitEvent @event) {
			Logger.LogDebug($"OnQuit: {@event}", tag: Tag);
			var player = InterEntities.LocalPlayer;
			if (player == null) {
				Logger.LogWarning("Local player not found for Quit event", tag: Tag);
				return;
			}

			player.Dispose();

			OnPlayerLeftOrQuitedHandler(player);
		}

		internal void OnPlayerJoinedHandler(JoinEvent @event) {
			Logger.LogDebug($"OnJoin: {@event} {@event.Player.Flags}", tag: Tag);
			var player = new RemotePlayer(InterEntities, @event.Player);

			if (player.IsLocal)
				player.Respawn();

			OnPlayerJoinedOrEnteredHandler(player);
		}

		private void OnPlayerJoinedOrEnteredHandler(Player player) {
			if (player.Reference.Flags.HasFlag(PlayerFlags.RoomMaster))
				InterEntities.MasterId = player.Id;

			Main.CoreAPI.EventAPI.Emit("session_player_joined", this, player);
			OnPlayerJoined.Invoke(player);

			foreach (var module in GetAllModules())
				module.OnPlayerJoined(player);
		}

		private void OnPlayerLeftOrQuitedHandler(Player player) {
			if (InterEntities.MasterId == player.Id)
				InterEntities.MasterId = Nox.Relay.Runtime.Entities.InvalidEntityId;

			Main.CoreAPI.EventAPI.Emit("session_player_left", this, player);
			OnPlayerLeft.Invoke(player);

			foreach (var module in GetAllModules())
				module.OnPlayerLeft(player);
		}

		internal void OnPlayerEnteredHandler(EnterResponse @event)
			=> OnPlayerEnteredHandler(@event, true);

		internal void OnPlayerEnteredHandler(EnterResponse @event, bool travel) {
			Logger.LogDebug($"OnEnter: {@event}", tag: Tag);
			var player = new LocalPlayer(InterEntities, @event.Player);
			InterEntities.LocalId = player.Id;

			Room = @event.Room;
			Room.Tps = @event.Tps;
			Room.Threshold = @event.Threshold;
			Room.RenderEntity = @event.RenderEntity;

			if (travel)
				Room.Traveling(TravelingRequest.Travel()).Forget();

			OnPlayerJoinedOrEnteredHandler(player);
		}

		internal void OnPlayerLeftHandler(LeaveEvent @event) {
			Logger.LogDebug($"OnLeave: {@event}", tag: Tag);
			var player = InterEntities.GetEntity<RemotePlayer>(@event.PlayerId);
			if (player == null) {
				Logger.LogWarning($"Player with ID {@event.PlayerId} not found for Leave event");
				return;
			}

			player.Dispose();

			OnPlayerLeftOrQuitedHandler(player);
		}

		internal void OnTransformHandler(TransformEvent @event) {
			if (@event.Type == TransformType.EntityPart) {
				// Handle player part transformation
				var player = InterEntities.GetEntity<Player>(@event.EntityId);
				if (player == null) {
					Logger.LogWarning($"Player with ID {@event.EntityId} not found for Transform event", tag: Tag);
					return;
				}

				// Update the remote player part if it's a remote player
				if (player is RemotePlayer remotePlayer) {
					// Get or create the part
					if (!remotePlayer.TryGetPart(@event.PartRig, out var part)) {
						Logger.LogDebug($"Creating new part {@event.PartRig} for remote player {@event.EntityId}", tag: Tag);
						part = new RemotePart(remotePlayer, @event.PartRig);
						remotePlayer.Parts[@event.PartRig] = part;
					}

					// Apply transform data from the event
					var transform = @event.Transform;
					if (transform.Flags.HasFlag(TransformFlags.Position))
						part.Position = transform.GetPosition();
					if (transform.Flags.HasFlag(TransformFlags.Rotation))
						part.Rotation = transform.GetRotation();
					if (transform.Flags.HasFlag(TransformFlags.Scale))
						part.Scale = transform.GetScale();
					if (transform.Flags.HasFlag(TransformFlags.Velocity))
						part.Velocity = transform.GetVelocity();
					if (transform.Flags.HasFlag(TransformFlags.Angular))
						part.Angular = transform.GetAngular();
				}
				else if (player is LocalPlayer) {
					// Local player transforms should be handled by controller, ignore remote updates
					// Only log if it's not from the local player itself
					if (@event.SenderId != player.Id)
						Logger.LogDebug($"Ignoring remote transform update for local player part {@event.PartRig}", tag: Tag);
				}
			}
			else if (@event.Type == TransformType.ByPath) {
				// Handle object transformation by path
				Logger.LogWarning($"Transform by path not yet implemented: Path={@event.Path}", tag: Tag);
				// TODO: Implement path-based transform when needed
			}
			else {
				Logger.LogWarning($"Unknown transform type: {@event.Type}", tag: Tag);
			}
		}

		internal void OnPropertiesHandler(PropertiesEvent @event) {
			var entity = InterEntities.GetEntity<Entity>(@event.EntityId);
			if (entity == null) {
				Logger.LogWarning($"Entity with ID {@event.EntityId} not found for Properties event", tag: Tag);
				return;
			}

			var sender = InterEntities.GetEntity<Entity>(@event.SenderId);
			if (sender == null) {
				Logger.LogWarning($"Entity with ID {@event.SenderId} not found for Properties event", tag: Tag);
				return;
			}

			var table = entity.Properties
				.ToDictionary(p => p.Key, p => p.Value);

			var fromLocal = entity.Id == sender.Id;

			foreach (var param in @event.Parameters) {
				if (!table.TryGetValue(param.Key, out var property)) {
					Logger.LogWarning($"Property with key {param.Key} not found for entity {entity.Id}, creating unassigned property.", tag: Tag);
					property = new UnassignedProperty(entity, param.Key, param.Value);
					entity.SetProperty(property);
					continue;
				}

				if (!property.Flags.HasFlag(fromLocal ? PropertyFlags.LocalEmit : PropertyFlags.RemoteEmit)) {
					Logger.LogWarning($"Ignoring non-synced property: {sender.Id} -> {entity.Id} ({property.Name ?? property.Key.ToString()})", tag: Tag);
					continue;
				}

				property.Deserialize(param.Value);
				property.IsDirty = false;
			}
		}

		internal void OnEventHandler(EventEvent @event) {
			Logger.LogDebug($"OnEvent: {@event} from SenderId={@event.SenderId}");
			var player = InterEntities.GetEntity<Player>(@event.SenderId);
			if (player == null) {
				Logger.LogWarning($"Player with ID {@event.SenderId} not found for Event event", tag: Tag);
				return;
			}

			Main.CoreAPI.EventAPI.Emit("session_event_triggered", this, @event.Name, @event.Payload, player);
			OnEventReceived.Invoke(@event.Name, @event.Payload, player);

			foreach (var module in GetAllModules())
				module.OnEvent(@event.Name, @event.Payload, player);
		}

		public void OnAuthorityTransferredHandler(Player @new, Player old) {
			Logger.LogDebug($"OnAuthorityTransferred: {old} -> {@new}", tag: Tag);

			Main.CoreAPI.EventAPI.Emit("session_authority_transferred", this, @new, old);
			OnAuthorityTransferred.Invoke(@new, old);

			foreach (var module in GetAllModules())
				module.OnAuthorityTransferred(@new, old);
		}

		public void OnEntityRegisteredHandler(IEntity entity) {
			Logger.LogDebug($"OnEntityRegistered: {entity}", tag: Tag);

			Main.CoreAPI.EventAPI.Emit("session_entity_registered", this, entity);
			OnEntityRegistered.Invoke(entity);

			foreach (var module in GetAllModules())
				module.OnEntityRegistered(entity);
		}

		public void OnEntityUnregisteredHandler(IEntity entity) {
			Logger.LogDebug($"OnEntityUnregistered: {entity}", tag: Tag);

			Main.CoreAPI.EventAPI.Emit("session_entity_unregistered", this, entity);
			OnEntityUnregistered.Invoke(entity);

			foreach (var module in GetAllModules())
				module.OnEntityUnregistered(entity);
		}

		public async UniTask<bool> OnTravelingAsync(TravelingEvent @event, bool response = true, Action<float, string> progress = null) {
			string hash;
			string url;

			var password = @event.Password;

			if (@event.UseUrl) {
				progress?.Invoke(0.1f, "Using provided URL for world travel");
				hash = BitConverter.ToString(@event.Hash)
					.Replace("-", "")
					.ToLowerInvariant();
				url = @event.DownloadUrl;
			}
			else if (@event.UseNode) {
				var identifier = @event.WorldIdentifier.ToString(Adapter.LastHandshake.MasterAddress);
				progress?.Invoke(0.1f, "Searching for master asset for world travel");
				Logger.LogDebug($"Searching {identifier}", tag: Tag);
				var req = new AssetSearchRequest {
					Engines = new[] { EngineExtensions.CurrentEngine.GetEngineName() },
					Platforms = new[] { PlatformExtensions.CurrentPlatform.GetPlatformName() },
					Versions = new[] { @event.WorldIdentifier.Version },
					Limit = 1
				};

				var asset = (await Main.WorldAPI.SearchAssets(
						WorldIdentifier.From(identifier),
						req
					))?.Assets
					.FirstOrDefault();

				if (asset == null) {
					progress?.Invoke(0.2f, $"No master asset found for world {identifier}");
					Logger.LogError($"No asset found for world {identifier}", tag: Tag);
					if (response)
						await OnTravelingFailed(@event, "No master asset found");
					return false;
				}

				hash = asset.Hash;
				url = asset.Url;
			}
			else {
				progress?.Invoke(0.1f, "The traveling does not contain valid URL or master asset information");
				Logger.LogError($"{@event} does not contain valid URL or master asset information", tag: Tag);
				if (response)
					await OnTravelingFailed(@event, "Invalid traveling information");
				return false;
			}

			progress?.Invoke(0.2f, "Searching for world");
			if (!Main.WorldAPI.HasInCache(hash)) {
				var download = Main.WorldAPI.DownloadToCache(
					url,
					hash: hash,
					progress: f => progress?.Invoke(0.2f + f * 0.45f, "Downloading world...")
				);
				await download.Start();
			}

			progress?.Invoke(0.65f, "Loading world");
			var scene = await Main.WorldAPI.LoadFromCache(
				hash,
				progress: f => progress?.Invoke(0.65f + f * 0.25f, "Loading world...")
			);
			if (scene == null) {
				progress?.Invoke(0.9f, "Failed to load scene for world");
				Logger.LogError($"Failed to load scene for world {@event.WorldIdentifier.ToString()}", tag: Tag);
				if (response)
					await OnTravelingFailed(@event, "Failed to load scene");
				return false;
			}

			scene.Identifier = @event.UseNode ? @event.WorldIdentifier : null;
			SetDimension(scene);
			progress?.Invoke(1f, "World loaded successfully");
			if (response)
				await OnTravelingSuccess(@event);
			return true;
		}

		public void OnTraveling(TravelingEvent @event)
			=> OnTravelingAsync(@event).Forget();

		private async UniTask OnTravelingFailed(TravelingEvent @event, string reason)
			=> await @event.Room.Traveling(TravelingRequest.Failed(reason));

		private async UniTask OnTravelingSuccess(TravelingEvent @event)
			=> await @event.Room.Traveling(TravelingRequest.Ready());


		public void OnAvatarChanged(AvatarChangedEvent @event)
			=> OnAvatarChangedAsync(@event).Forget();

		private async UniTask OnAvatarChangedAsync(AvatarChangedEvent @event) {
			if (@event.Result != AvatarChangedResult.Changing) return;
			var player = InterEntities.GetEntity<Player>(@event.PlayerId);
			if (player == null) return;
			await player.SetAvatar(@event.AvatarIdentifier);
		}
	}
}