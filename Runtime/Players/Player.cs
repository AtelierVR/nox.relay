using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Nox.Avatars.Parameters;
using Nox.Avatars.Players;
using Nox.CCK.Players;
using Nox.CCK.Utils;
using Nox.Entities;
using Nox.Audio.Players;
using Nox.Players;
using Nox.Relay.Core.Rooms;
using Nox.Relay.Core.Types.Transform;
using Nox.Worlds.Spawns;
using UnityEngine;
using Logger = Nox.CCK.Utils.Logger;
using CorePlayer = Nox.Relay.Core.Players.Player;
using Nox.CCK.Events;

namespace Nox.Relay.Runtime.Players {

	public abstract class Player : Entity, IPlayer, IPlayerAvatar, IPlayerVoice {

		protected Player(Entities context, CorePlayer player) : base(context, player.Id) {
			Reference  = player;
			Identifier = player.Identifier;
			Data = Main.PlayerAPI.Get(Identifier);
			Data.OnChanged.AddListener(OnDataChanged);

			if (Main.VoiceRegister != null) {
				Main.VoiceRegister.OnVolume.AddListener(OnVolumeChanged);
				Main.VoiceRegister.OnMute.AddListener(OnMuteChanged);
			}
			OnVolume.Invoke(Volume, EffectiveVolume);
			OnMute.Invoke(IsMuted, IsEffectivelyMuted);
		}

        public override void Dispose() {
			Data.OnChanged.RemoveListener(OnDataChanged);

			if (Main.VoiceRegister != null) {
				Main.VoiceRegister.OnVolume.RemoveListener(OnVolumeChanged);
				Main.VoiceRegister.OnMute.RemoveListener(OnMuteChanged);
			}

			Data.Dispose();
			base.Dispose();
		}

		public readonly CorePlayer Reference;

		readonly internal Dictionary<ushort, IPart> Parts = new();

		public IPart[] GetParts()
			=> Parts.Values.ToArray();

		public bool TryGetPart(ushort name, out IPart part) {
			if (Parts.TryGetValue(name, out var p)) {
				part = p;
				return true;
			}

			part = null;
			return false;
		}

		public string Display {
			get => Reference?.Display ?? $"Player #{Id}";
			set {
				if (Reference != null)
					Reference.Display = value;
				// TODO: Send update to server
			}
		}

		public Identifier Identifier { get; }

		public bool IsMaster
			=> Context.MasterId == Id;

		public abstract bool IsLocal { get; }

		public void Teleport(Vector3 position, Quaternion rotation) {
			Position = position;
			Rotation = rotation;
			Velocity = Vector3.zero;
			Angular  = Vector3.zero;
		}

		public void Respawn() {
			if (Context.Context.Dimensions
					.GetDescriptor(Dimensions.MainIndex)
					.GetModules()
					.FirstOrDefault(e => e is ISpawnModule)
				is not ISpawnModule module) {
				Logger.LogWarning("No spawn module found for respawning player.");
				return;
			}

			var spawn = module.ChoiceSpawn();
			Teleport(spawn.Position, spawn.Rotation);
			Logger.Log($"Player {Id} respawned to {spawn.Position}.");
		}

		public Vector3 Position {
			get => Parts.TryGetValue(PlayerRig.Base.ToIndex(), out var p) ? p.Position : Vector3.zero;
			set {
				if (!Parts.TryGetValue(PlayerRig.Base.ToIndex(), out var p))
					return;
				p.Position = value;
			}
		}

		public Quaternion Rotation {
			get => Parts.TryGetValue(PlayerRig.Base.ToIndex(), out var p) ? p.Rotation : Quaternion.identity;
			set {
				if (!Parts.TryGetValue(PlayerRig.Base.ToIndex(), out var p))
					return;
				p.Rotation = value;
			}
		}

		public Vector3 Scale {
			get => Parts.TryGetValue(PlayerRig.Base.ToIndex(), out var p) ? p.Scale : Vector3.one;
			set {
				if (!Parts.TryGetValue(PlayerRig.Base.ToIndex(), out var p))
					return;
				p.Scale = value;
			}
		}

		public Vector3 Velocity {
			get => Parts.TryGetValue(PlayerRig.Base.ToIndex(), out var p) ? p.Velocity : Vector3.zero;
			set {
				if (!Parts.TryGetValue(PlayerRig.Base.ToIndex(), out var p))
					return;
				p.Velocity = value;
			}
		}

		public Vector3 Angular {
			get => Parts.TryGetValue(PlayerRig.Base.ToIndex(), out var p) ? p.Angular : Vector3.zero;
			set {
				if (!Parts.TryGetValue(PlayerRig.Base.ToIndex(), out var p))
					return;
				p.Angular = value;
			}
		}

		public override string ToString() {
			try {
				return $"{GetType().Name}[Id={Id}, Display={Display}, Identifier={Identifier.ToString()}, IsMaster={IsMaster}, IsLocal={IsLocal}]";
			} catch {
				// During construction, some properties may not be initialized yet
				return $"{GetType().Name}[Id={Id}, <initializing>]";
			}
		}

		#region IPlayerVoice — Volume & Mute

		public IPlayerData Data;

		/// <inheritdoc />
		public float Volume {
			get => Data.Get("volume", 1f);
			set => Data.Set("volume", Mathf.Clamp(value, 0f, 2f));
		}

		public readonly NoxEvent<float, float> OnVolume = new();

		/// <inheritdoc />
		public bool IsMuted {
			get => Data.Get("mute", false);
			set => Data.Set("mute", value);
		}

		public readonly NoxEvent<bool, bool> OnMute = new();

		/// <inheritdoc />
		public float EffectiveVolume
			=> Main.VoiceRegister != null
				? Mathf.Min(Volume, Main.VoiceRegister.Channel.EffectiveVolume)
				: Volume;

		/// <inheritdoc />
		public bool IsEffectivelyMuted
			=> IsMuted || (Main.VoiceRegister?.Channel.IsEffectivelyMuted ?? false);

		private void OnDataChanged(string[] key, object @new, object @old) {
			if(key.Length == 1 && key[0] == "volume")
				OnVolume.Invoke(Volume, EffectiveVolume);
			else if(key.Length == 1 && key[0] == "mute")
				OnMute.Invoke(IsMuted, IsEffectivelyMuted);
		}

        private void OnVolumeChanged(float local, float effective)
			=> OnVolume.Invoke(Volume, EffectiveVolume);

        private void OnMuteChanged(bool local, bool effective)
			=> OnMute.Invoke(IsMuted, IsEffectivelyMuted);

		#endregion

		protected internal Identifier Avatar = Identifier.Invalid;

		public virtual Identifier GetAvatar()
			=> Avatar;

		public virtual UniTask<bool> SetAvatar(Identifier identifier) {
			Logger.LogDebug($"Changing avatar for {this} to {identifier.ToString()}", tag: GetType().Name);
			Avatar = identifier;
			return UniTask.FromResult(true);
		}

		#region Common Avatar Parameter Synchronization

		/// <summary>
		/// Synchronizes avatar parameters as Entity properties.
		/// Creates AvatarParameterProperty for each parameter with appropriate flags.
		/// </summary>
		/// <param name="parameters">Array of avatar parameters to synchronize</param>
		/// <param name="isLocal"></param>
		protected void SynchronizeAvatarParameters(IParameter[] parameters, bool isLocal) {
			var paramKeys = new HashSet<int>();

			if (parameters?.Length > 0) {
				foreach (var param in parameters) {
					var flags         = param.GetFlags();
					var propertyFlags = PropertyFlags.None;

					if (flags.HasFlag(ParameterFlags.OwnerSyncsToViewers))
						propertyFlags |= isLocal ? PropertyFlags.LocalEmit : PropertyFlags.RemoteEmit;
					if (flags.HasFlag(ParameterFlags.ViewerSyncsToOwner))
						propertyFlags |= isLocal ? PropertyFlags.RemoteEmit : PropertyFlags.LocalEmit;

					var key = param.GetKey();
					paramKeys.Add(key);

					// Check if property already exists
					if (Properties.TryGetValue(key, out var existingProp)) {
						// Update existing property if it's an AvatarParameterProperty
						if (existingProp is AvatarParameterProperty avatarProp)
							avatarProp.UpdateCache();
						else if (existingProp is UnassignedProperty) {
							// Replace unassigned property with AvatarParameterProperty
							var newProp = new AvatarParameterProperty(this, param, propertyFlags);
							SetProperty(newProp);
							Logger.LogDebug($"Replaced unassigned property for parameter {param.GetName()} (key={key}, flags={flags}) with propertyFlags={propertyFlags}", tag: GetType().Name);
						}
					} else {
						// Create new AvatarParameterProperty
						var newProp = new AvatarParameterProperty(this, param, propertyFlags);
						SetProperty(newProp);
						Logger.LogDebug($"Created property for parameter {param.GetName()} (key={key}, flags={flags}) with propertyFlags={propertyFlags}", tag: GetType().Name);
					}
				}
			}

			// Remove AvatarParameterProperty entries no longer present in the avatar's parameter list
			foreach (var key in Properties.Keys.ToList())
				if (Properties[key] is AvatarParameterProperty && !paramKeys.Contains(key)) {
					Properties.Remove(key);
					Logger.LogDebug($"Removed avatar parameter property (key={key}) no longer present in avatar.", tag: GetType().Name);
				}
		}

		#endregion

		#region Common Transform Sending (LocalPlayer only)

		// Prevents concurrent execution of SendTransformsIfNeeded()
		private bool _startTransform;

		public override void Tick() {
			base.Tick();

			// Only LocalPlayer sends transforms
			if (IsLocal && !_startTransform)
				SendTransformsIfNeeded().Forget();
		}

		/// <summary>
		/// Sends transform updates for parts that have moved beyond the threshold.
		/// Called from Tick() which is already rate-limited by the Room's TPS.
		/// Only executed for LocalPlayer.
		/// </summary>
		async protected UniTask SendTransformsIfNeeded() {
			if (!IsLocal)
				return; // Safety check

			_startTransform = true;

			var room = Context?.Context.Room;
			if (room == null)
				goto end;

			// Need active controller to read current transform values
			var controller = Main.ControllerAPI.Current;
			if (controller == null)
				goto end;

			// Collect parts that need updates (moved beyond threshold)
			var dirtyParts = new List<(ushort partId, TransformObject delta)>();

			// Ensure a sensible minimum threshold to avoid floating point precision issues
			var threshold = room.Threshold < 0.0001f ? 0.0001f : room.Threshold;

			foreach (var (index, value) in Parts) {
				if (value is not Part cachedPart)
					continue;

				// Get current transform from controller
				if (!controller.TryGetPart(index, out var currentTransform))
					continue;

				// Compare current vs cached and get only changed values exceeding threshold
				var deltaTransform = GetChangedTransform(cachedPart, currentTransform, threshold);
				if (deltaTransform == null)
					continue;

				dirtyParts.Add((partId: index, deltaTransform));

				// Update cache with values we're about to send
				if (deltaTransform.Flags.HasFlag(TransformFlags.Position))
					cachedPart.SetPosition(deltaTransform.GetPosition());
				if (deltaTransform.Flags.HasFlag(TransformFlags.Rotation))
					cachedPart.SetRotation(deltaTransform.GetRotation());
				if (deltaTransform.Flags.HasFlag(TransformFlags.Scale))
					cachedPart.SetScale(deltaTransform.GetScale());
				if (deltaTransform.Flags.HasFlag(TransformFlags.Velocity))
					cachedPart.SetVelocity(deltaTransform.GetVelocity());
				if (deltaTransform.Flags.HasFlag(TransformFlags.Angular))
					cachedPart.SetAngular(deltaTransform.GetAngular());
			}

			// Send all changes in a batch
			if (dirtyParts.Count > 0)
				await SendTransformsBatch(room, dirtyParts);

		end:
			_startTransform = false;
		}

		/// <summary>
		/// Compares current transform with cached values and builds a delta TransformObject.
		/// </summary>
		private static TransformObject GetChangedTransform(Part cached, TransformObject current, float threshold) {
			var delta = new TransformObject();

			if (current.Flags.HasFlag(TransformFlags.Position) && !current.IsSamePosition(cached.GetPosition(), threshold))
				delta.SetPosition(current.GetPosition());

			if (current.Flags.HasFlag(TransformFlags.Rotation) && !current.IsSameRotation(cached.GetRotation(), threshold))
				delta.SetRotation(current.GetRotation());

			if (current.Flags.HasFlag(TransformFlags.Scale) && !current.IsSameScale(cached.GetScale(), threshold))
				delta.SetScale(current.GetScale());

			if (current.Flags.HasFlag(TransformFlags.Velocity) && !current.IsSameVelocity(cached.GetVelocity(), threshold))
				delta.SetVelocity(current.GetVelocity());

			if (current.Flags.HasFlag(TransformFlags.Angular) && !current.IsSameAngular(cached.GetAngular(), threshold))
				delta.SetAngular(current.GetAngular());

			return delta.Flags != TransformFlags.None ? delta : null;
		}

		/// <summary>
		/// Sends a batch of transform updates to the room in parallel.
		/// </summary>
		private static async UniTask SendTransformsBatch(Room room, List<(ushort partId, TransformObject delta)> dirtyParts) {
			var tasks = new List<UniTask<bool>>();

			foreach (var (partId, deltaTransform) in dirtyParts) {
				var request = TransformRequest.CreateEntity(ushort.MaxValue, partId, deltaTransform);
				tasks.Add(room.Transform(request));
			}

			await UniTask.WhenAll(tasks);
		}

		#endregion

		#region Physical

		override protected void OnPhysicalCreated() {
			Context.Context.OnPlayerVisibilityChangedHandler(this, true);
		}

		override protected void OnPhysicalDestroyed() {
			Context.Context.OnPlayerVisibilityChangedHandler(this, false);
			SynchronizeAvatarParameters(null, IsLocal);
		}

		#endregion

		#region IPlayerVoice Implementation

		/// <summary>Current voice audio source. Set by VoiceReceiver (remote) or MicrophoneConnector (local).</summary>
		protected ICapturedAudio _audio;

		public ListenMode Listen { get; set; } = ListenMode.Normal;

		public SpeakMode Speak { get; set; } = SpeakMode.Normal;

		public virtual ICapturedAudio Audio => _audio;

		/// <summary>
		/// Speaking indicator for UI. Updated externally by NoxVoiceChat.
		/// </summary>
		public bool IsSpeaking { get; set; }

		public virtual LevelFlags Level {
			get {
				if (!IsSpeaking) return LevelFlags.None;
				return LevelFlags.Speaking | Speak switch {
					SpeakMode.Whisper   => LevelFlags.Whisper,
					SpeakMode.Broadcast => LevelFlags.Broadcast,
					_                   => LevelFlags.Normal
				};
			}
		}

		#endregion

		#region Session Lifecycle Hooks

		/// <summary>Called when this player joins the session (remote players).</summary>
		public virtual void OnJoined() { }

		/// <summary>Called when this player enters the session (local player).</summary>
		public virtual void OnEntered() { }

		/// <summary>Called when this player leaves the session.</summary>
		public virtual void OnLeft() { }

		/// <summary>Called when this player quits the session.</summary>
		public virtual void OnQuit() { }

		#endregion


	}
}