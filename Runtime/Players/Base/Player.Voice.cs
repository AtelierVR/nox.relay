using Cysharp.Threading.Tasks;
using Nox.Audio.Players;
using Nox.Avatars.Parameters;
using Nox.CCK;
using Nox.CCK.Events;
using Nox.CCK.Network;
using Nox.Entities;
using Nox.Relay.Core.Types.Stream;
using UnityEngine;

namespace Nox.Relay.Runtime.Players {
	/// <summary>
	/// Voice of a player: the <see cref="IPlayerVoice"/> state (volume, mute, level and the live
	/// <see cref="ICapturedAudio"/>) plus the dispatch of the stream packets redirected by the session.
	/// Each concrete player implements its own side in <c>LocalPlayer.Voice.cs</c> (emission) and
	/// <c>RemotePlayer.Voice.cs</c> (playback).
	/// </summary>
	public abstract partial class Player {
		#region IPlayerVoice — Volume & Mute

		/// <inheritdoc />
		public float Volume {
			get => Persistent.Get("volume", 1f);
			set => Persistent.Set("volume", Mathf.Clamp(value, 0f, 2f));
		}

		public readonly NoxEvent<float, float> OnVolume = new();

		/// <inheritdoc />
		public bool IsMuted {
			get => Persistent.Get("mute", false);
			set {
				if (IsMuted == value)
					return;

				Persistent.Set("mute", value);

				// A listener mute is private: only the muted player is told, through a targeted
				// event (the same mute is never broadcast to the rest of the room).
				if (!IsLocal)
					NotifyMuted(value);

				// The local view of a remote avatar must show the mute we just applied.
				ApplyVoiceStatusToAvatar();
			}
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

		private void OnVolumeChanged(float local, float effective)
			=> OnVolume.Invoke(Volume, EffectiveVolume);

		private void OnMuteChanged(bool local, bool effective)
			=> OnMute.Invoke(IsMuted, IsEffectivelyMuted);

		#endregion

		#region Voice status (self-mute / deafen)

		/// <summary>Property carrying the announced self-mute/deafen flags of a player.</summary>
		private const string VoiceStatusProperty = "voice.status";

		/// <summary>Avatar parameter carrying the self-mute state (driven by the session).</summary>
		private const string MutedParameter = "Muted";

		/// <summary>Avatar parameter carrying the deafen state (driven by the session).</summary>
		private const string DeafenedParameter = "Deafened";

		/// <summary>Name of the targeted event telling a player it has been muted by someone.</summary>
		internal const string VoiceMuteEventName = "voice.mute";

		private static readonly int  VoiceStatusKey = VoiceStatusProperty.Hash();
		internal static readonly long VoiceMuteEventKey = VoiceMuteEventName.Hash();

		[System.Flags]
		private enum VoiceStatusFlags : byte {
			None      = 0,
			SelfMuted = 1 << 0,
			Deafened  = 1 << 1,

			// Speak mode (0..4) rides bits 2..4; the remaining bits stay free.
			ModeShift = 2,
			ModeMask  = 0b0001_1100
		}

		/// <summary>
		/// Whether this player's own microphone is muted. Remote players override it with the value
		/// announced by their owner; the local player derives it from its <see cref="Speak"/> mode.
		/// </summary>
		public virtual bool IsSelfMuted
			=> Speak == SpeakMode.Muted;

		/// <summary>
		/// Whether this player is deafened. Remote players override it with the value announced by
		/// their owner; the local player derives it from the local deafen setting.
		/// </summary>
		public virtual bool IsDeafened
			=> false;

		/// <summary>Raised when the announced status (<c>selfMuted</c>, <c>deafened</c>) changes.</summary>
		public readonly NoxEvent<bool, bool> OnVoiceStatus = new();

		private readonly NoxEvent<bool> _onSelfMuteChanged = new();
		private readonly NoxEvent<bool> _onDeafenChanged  = new();

		/// <inheritdoc />
		public NoxEvent<bool> OnSelfMuteChanged
			=> _onSelfMuteChanged;

		/// <inheritdoc />
		public NoxEvent<bool> OnDeafenChanged
			=> _onDeafenChanged;

		/// <summary>Mute shown for this player on the local client (self-mute or our own mute).</summary>
		public bool IsMutedForView
			=> IsSelfMuted || (!IsLocal && IsMuted);

		private Property _voiceStatusProperty;
		private bool     _voiceStatusInitialized;
		private bool     _announcedSelfMuted;
		private bool     _announcedDeafened;
		private SpeakMode _announcedSpeak;

		/// <summary>
		/// Declares the status property, so the owner can publish it and a remote client can bind
		/// the incoming value. Called once from the player constructor.
		/// </summary>
		private void SetupVoiceStatus() {
			if (_voiceStatusProperty != null)
				return;

			OnParameterSync.AddListener(OnVoiceParameterSync);

			_voiceStatusProperty = new Property(
				this,
				VoiceStatusProperty,
				(byte)VoiceStatusFlags.None,
				IsLocal ? PropertyFlags.LocalEmit : PropertyFlags.RemoteEmit
			);

			SetProperty(_voiceStatusProperty);

			// The owner must publish its initial state even when it is "nothing".
			_voiceStatusProperty.IsDirty = IsLocal;
		}

		/// <summary>
		/// Publishes the local status when it changes. The property is rate-limited by the room's
		/// TPS and periodically re-sent by the relay, so this is cheap to poll every frame.
		/// </summary>
		internal void TickVoiceStatus() {
			if (!IsLocal)
				return;

			var selfMuted = IsSelfMuted;
			var deafened  = IsDeafened;
			var speak     = Speak;

			if (_voiceStatusInitialized
				&& selfMuted == _announcedSelfMuted
				&& deafened == _announcedDeafened
				&& speak == _announcedSpeak)
				return;

			if (_voiceStatusInitialized) {
				if (selfMuted != _announcedSelfMuted)
					OnSelfMuteChanged.Invoke(selfMuted);
				if (deafened != _announcedDeafened)
					OnDeafenChanged.Invoke(deafened);
			}

			_voiceStatusInitialized = true;
			_announcedSelfMuted     = selfMuted;
			_announcedDeafened      = deafened;
			_announcedSpeak         = speak;

			if (_voiceStatusProperty == null)
				SetupVoiceStatus();

			var flags = (selfMuted ? VoiceStatusFlags.SelfMuted : VoiceStatusFlags.None)
				| (deafened ? VoiceStatusFlags.Deafened : VoiceStatusFlags.None)
				| (VoiceStatusFlags)(((byte)speak << (int)VoiceStatusFlags.ModeShift) & (byte)VoiceStatusFlags.ModeMask);

			_voiceStatusProperty.Value   = (byte)flags;
			_voiceStatusProperty.IsDirty = true;

			OnVoiceStatus.Invoke(selfMuted, deafened);
			Main.CoreAPI.EventAPI.Emit("voice.status_changed", this, selfMuted, deafened, (int)speak);
			ApplyVoiceStatusToAvatar();
		}

		/// <summary>Applies an incoming status property of a remote player.</summary>
		internal virtual void ApplyVoiceStatus(byte flags) {
			var selfMuted = (flags & (byte)VoiceStatusFlags.SelfMuted) != 0;
			var deafened  = (flags & (byte)VoiceStatusFlags.Deafened) != 0;
			var speak     = (SpeakMode)((flags & (byte)VoiceStatusFlags.ModeMask) >> (int)VoiceStatusFlags.ModeShift);

			if (selfMuted == IsSelfMuted && deafened == IsDeafened && speak == Speak)
				return;

			var wasSelfMuted = IsSelfMuted;
			var wasDeafened  = IsDeafened;

			SetRemoteVoiceStatus(selfMuted, deafened);

			// Keep the announced mode on the remote player so UI (and the level flags) reflect it.
			Speak = speak;

			if (selfMuted != wasSelfMuted)
				OnSelfMuteChanged.Invoke(selfMuted);
			if (deafened != wasDeafened)
				OnDeafenChanged.Invoke(deafened);

			OnVoiceStatus.Invoke(selfMuted, deafened);
			ApplyVoiceStatusToAvatar();
		}

		/// <summary>Stores the announced status of a remote player (overridden by <see cref="RemotePlayer"/>).</summary>
		protected virtual void SetRemoteVoiceStatus(bool selfMuted, bool deafened) { }

		/// <summary>
		/// The voice status is carried by the dedicated <c>voice.status</c> property (public
		/// self-mute/deafen, private listener mutes), so its avatar parameters never need a
		/// second network path.
		/// </summary>
		private static void OnVoiceParameterSync(ParameterSyncEventArgs args) {
			var name = args.Parameter?.Name;
			if (name != MutedParameter && name != DeafenedParameter)
				return;

			args.Flags = PropertyFlags.None;
		}

		/// <summary>Dispatches a received property to the voice status when it is its own.</summary>
		internal virtual void OnPropertyUpdated(IProperty property) {
			if (IsLocal || property.Key != VoiceStatusKey)
				return;

			var raw   = property.Value;
			var flags = raw is byte[] { Length: > 0 } bytes ? bytes[0] : raw.ToByte();
			ApplyVoiceStatus(flags);
		}

		/// <summary>
		/// Pushes the current status onto the avatar parameters <c>Muted</c> and <c>Deafened</c>
		/// when the loaded avatar declares them (they are read-only from the relay point of view).
		/// </summary>
		protected void ApplyVoiceStatusToAvatar() {
			var module = ParameterModule;
			if (module == null)
				return;

			var muted = IsMutedForView;
			if (module.GetParameter(MutedParameter) is { ValueType: ParameterType.Bool } mutedParameter
				&& mutedParameter.Value is bool currentMuted
				&& currentMuted != muted)
				mutedParameter.Value = muted;

			var deafened = IsDeafened;
			if (module.GetParameter(DeafenedParameter) is { ValueType: ParameterType.Bool } deafenedParameter
				&& deafenedParameter.Value is bool currentDeafened
				&& currentDeafened != deafened)
				deafenedParameter.Value = deafened;
		}

		/// <summary>Tells the owner of this (remote) player that we muted or unmuted them.</summary>
		internal void NotifyMuted(bool muted) {
			var session = Context?.Context;
			if (session?.Room == null)
				return;

			session.EmitEventTargeted(
				VoiceMuteEventName,
				new[] { (byte)(muted ? 1 : 0) },
				Id
			).Forget();
		}

		#endregion

		#region IPlayerVoice Implementation

		/// <summary>
		/// Live audio source: the microphone for the local player, the received playback for a remote
		/// one. Returns <c>null</c> while no voice is active.
		/// </summary>
		protected ICapturedAudio _audio;

		/// <inheritdoc />
		public ListenMode Listen { get; set; } = ListenMode.Normal;

		/// <inheritdoc />
		public SpeakMode Speak { get; set; } = SpeakMode.Normal;

		/// <inheritdoc />
		public virtual ICapturedAudio Audio {
			get => _audio;
			set => _audio = value;
		}

		/// <summary>Speaking indicator for UI, updated by the voice pipeline.</summary>
		public bool IsSpeaking { get; set; }

		/// <inheritdoc />
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

		#region Packet handling

		/// <summary>Dispatches a voice packet addressed to this player.</summary>
		internal void OnVoiceStream(StreamEvent @event) {
			switch (@event.SubType) {
				case StreamSubType.Sample:
					// Voice audio rides channel 0; other channels (e.g. video) are out of scope here.
					if (@event.ChannelId != ChannelId.Voice)
						return;

					OnVoiceSample(@event);
					break;

				case StreamSubType.Control:
					OnVoiceControl(@event);
					break;
			}
		}

		/// <summary>Incoming audio sample — implemented by <see cref="RemotePlayer"/>.</summary>
		protected virtual void OnVoiceSample(StreamEvent @event) { }

		/// <summary>Hearing gate for a speaker — implemented by <see cref="RemotePlayer"/>.</summary>
		protected virtual void OnVoiceControl(StreamEvent @event) { }

		#endregion
	}
}
