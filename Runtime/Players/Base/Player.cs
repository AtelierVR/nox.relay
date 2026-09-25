using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Nox.Avatars.Players;
using Nox.CCK.Players;
using Nox.CCK.Utils;
using Nox.Entities;
using Nox.Audio.Players;
using Nox.Players;
using Nox.Relay.Runtime.Voice;
using Nox.Worlds.Spawns;
using UnityEngine;
using Logger = Nox.CCK.Utils.Logger;
using CorePlayer = Nox.Relay.Core.Players.Player;
using Nox.CCK.Events;

namespace Nox.Relay.Runtime.Players {

	public abstract partial class Player : Entity, IPlayer, IPlayerAvatar, IPlayerVoice {

		public readonly CorePlayer Reference;

		public Identifier Identifier { get; }

		protected Player(Entities context, CorePlayer player) : base(context, player.Id) {
			Reference  = player;
			Identifier = player.Identifier;
			Data = Main.PlayerAPI.Get(Identifier);
			Data.OnChanged.AddListener(OnDataChanged);
			Main.VoiceRegister?.OnVolume.AddListener(OnVolumeChanged);
			Main.VoiceRegister?.OnMute.AddListener(OnMuteChanged);
			OnVolume.Invoke(Volume, EffectiveVolume);
			OnMute.Invoke(IsMuted, IsEffectivelyMuted);
		}

        public override void Dispose() {
			Data.OnChanged.RemoveListener(OnDataChanged);
			Main.VoiceRegister?.OnVolume.RemoveListener(OnVolumeChanged);
			Main.VoiceRegister?.OnMute.RemoveListener(OnMuteChanged);
			Data.Dispose();
			base.Dispose();
		}


		/// <summary>
		/// The voice pipeline for this player (local microphone capture or remote playback).
		/// Assigned by <see cref="LocalPlayer"/> / <see cref="RemotePlayer"/>.
		/// </summary>
		public VoiceProvider VoiceProvider { get; protected set; }

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

		/// <summary>
		/// Platform announced by this player, or <see cref="Platform.None"/> when unknown.
		/// Filled from the join event for a remote player, from the running client for the local one.
		/// </summary>
		public Platform Platform { get; internal set; } = Platform.None;

		/// <summary>
		/// Game engine announced by this player, or <see cref="Engine.None"/> when unknown.
		/// Filled from the join event for a remote player, from the running client for the local one.
		/// </summary>
		public Engine Engine { get; internal set; } = Engine.None;



		public bool IsMaster
			=> Context.MasterId == Id;

		public abstract bool IsLocal { get; }

		protected abstract IPart CreatePart(ushort index);

		public IPart GetOrCreatePart(ushort index) {
			if (!Parts.TryGetValue(index, out var p)) {
				p = CreatePart(index);
				Parts[index] = p;
			}
			return p;
		}

		public void Teleport(Vector3 position, Quaternion rotation) {
			Position = position;
			Rotation = rotation;
			Velocity = Vector3.zero;
			Angular  = Vector3.zero;
		}

		public bool NeedsRespawn { get; internal set; }

		public void Respawn() {
			var descriptor = Context.Context.Dimensions?.GetDescriptor(Dimensions.MainIndex);
			if (descriptor == null) {
				NeedsRespawn = true;
				return;
			}

            if (descriptor.GetModules().FirstOrDefault(e => e is ISpawnModule) is not ISpawnModule module) {
                Logger.LogWarning("No spawn module found for respawning player.");
                NeedsRespawn = false;
                return;
            }

            NeedsRespawn = false;
			var spawn = module.ChoiceSpawn();
			Teleport(spawn.Position, spawn.Rotation);
			Logger.Log($"Player {Id} respawned to {spawn.Position}.");
		}

		public Vector3 Position {
			get => Parts.TryGetValue(PlayerRig.Base.ToIndex(), out var p) ? p.Position : Vector3.zero;
			set => GetOrCreatePart(PlayerRig.Base.ToIndex()).Position = value;
		}

		public Quaternion Rotation {
			get => Parts.TryGetValue(PlayerRig.Base.ToIndex(), out var p) ? p.Rotation : Quaternion.identity;
			set => GetOrCreatePart(PlayerRig.Base.ToIndex()).Rotation = value;
		}

		public Vector3 Scale {
			get => Parts.TryGetValue(PlayerRig.Base.ToIndex(), out var p) ? p.Scale : Vector3.one;
			set => GetOrCreatePart(PlayerRig.Base.ToIndex()).Scale = value;
		}

		public Vector3 Velocity {
			get => Parts.TryGetValue(PlayerRig.Base.ToIndex(), out var p) ? p.Velocity : Vector3.zero;
			set => GetOrCreatePart(PlayerRig.Base.ToIndex()).Velocity = value;
		}

		public Vector3 Angular {
			get => Parts.TryGetValue(PlayerRig.Base.ToIndex(), out var p) ? p.Angular : Vector3.zero;
			set => GetOrCreatePart(PlayerRig.Base.ToIndex()).Angular = value;
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
				? Volume * Main.VoiceRegister.Channel.EffectiveVolume
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

		#region Physical

		override protected void OnPhysicalCreated() {
			Context.Context.OnPlayerVisibilityChangedHandler(this, true);
		}

		override protected void OnPhysicalDestroyed() {
			Context.Context.OnPlayerVisibilityChangedHandler(this, false);
			// The avatar goes away with the physical: Unbind() also detaches the parameter bindings,
			// instead of leaving properties that would query a disposed playable.
			Unbind();
		}

		#endregion

		#region IPlayerVoice Implementation

		/// <summary>Current voice audio source. Set by VoiceReceiver (remote) or MicrophoneConnector (local).</summary>
		protected ICapturedAudio _audio;

		public ListenMode Listen { get; set; } = ListenMode.Normal;

		public SpeakMode Speak { get; set; } = SpeakMode.Normal;

		public virtual ICapturedAudio Audio => _audio;

		/// <summary>
		/// Speaking indicator for UI. Updated by the player's voice provider.
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