using Nox.Audio.Players;
using Nox.CCK.Events;
using Nox.Players;
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
			if (key.Length == 1 && key[0] == "volume")
				OnVolume.Invoke(Volume, EffectiveVolume);
			else if (key.Length == 1 && key[0] == "mute")
				OnMute.Invoke(IsMuted, IsEffectivelyMuted);
		}

		private void OnVolumeChanged(float local, float effective)
			=> OnVolume.Invoke(Volume, EffectiveVolume);

		private void OnMuteChanged(bool local, bool effective)
			=> OnMute.Invoke(IsMuted, IsEffectivelyMuted);

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
