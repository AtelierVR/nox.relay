using Nox.Microphone.Players;
using UnityEngine;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>
	/// Configuration parameters for each voice mode.
	/// Created as a ScriptableObject asset so it can be tuned in the editor.
	/// </summary>
	[CreateAssetMenu(fileName = "VoiceConfig", menuName = "Nox/Voice Config")]
	public class VoiceConfig : ScriptableObject {
		[Header("Whisper")]
		[Tooltip("Maximum hearing distance in meters.")]
		public float WhisperRange = 3f;

		[Tooltip("Volume multiplier applied at zero distance.")]
		[Range(0f, 2f)]
		public float WhisperVolume = 0.3f;

		[Tooltip("Spatial blend: 1 = fully 3D, 0 = fully 2D.")]
		[Range(0f, 1f)]
		public float WhisperSpatialBlend = 1f;

		[Tooltip("Audio rolloff mode for 3D spatialization.")]
		public AudioRolloffMode WhisperRolloff = AudioRolloffMode.Logarithmic;

		[Header("Normal")]
		public float NormalRange = 20f;

		[Range(0f, 2f)]
		public float NormalVolume = 1f;

		[Range(0f, 1f)]
		public float NormalSpatialBlend = 1f;

		public AudioRolloffMode NormalRolloff = AudioRolloffMode.Linear;

		[Header("Broadcast")]
		[Tooltip("Broadcast has infinite range — heard by everyone regardless of distance.")]
		public float BroadcastVolume = 1f;

		[Range(0f, 1f)]
		[Tooltip("Broadcast is 2D (non-spatial) by default.")]
		public float BroadcastSpatialBlend = 0f;

		[Header("Loud")]
		[Tooltip("Maximum hearing distance in meters for Loud mode.")]
		public float LoudRange = 40f;

		[Range(0f, 2f)]
		public float LoudVolume = 1.2f;

		[Range(0f, 1f)]
		public float LoudSpatialBlend = 1f;

		public AudioRolloffMode LoudRolloff = AudioRolloffMode.Linear;

		[Header("Audio")]
		[Tooltip("Sample rate for encoding/decoding (must match Opus encoder).")]
		public int SampleRate = 48000;

		[Tooltip("Frame size in samples per channel (must match Opus encoder).")]
		public int FrameSize = 960;

		[Tooltip("Opus bitrate (bps).")]
		public int Bitrate = 32000;

		/// <summary>
		/// Get the maximum hearing range for a given speak mode.
		/// Returns <see cref="float.MaxValue"/> for Broadcast, 0 for Muted.
		/// </summary>
		public float GetRange(SpeakMode mode) {
			return mode switch {
				SpeakMode.Whisper   => WhisperRange,
				SpeakMode.Normal    => NormalRange,
				SpeakMode.Loud      => LoudRange,
				SpeakMode.Broadcast => float.MaxValue,
				SpeakMode.Muted     => 0f,
				_                   => NormalRange
			};
		}

		/// <summary>Get the volume multiplier for a given speak mode.</summary>
		public float GetVolume(SpeakMode mode) {
			return mode switch {
				SpeakMode.Whisper   => WhisperVolume,
				SpeakMode.Normal    => NormalVolume,
				SpeakMode.Loud      => LoudVolume,
				SpeakMode.Broadcast => BroadcastVolume,
				_                   => NormalVolume
			};
		}

		/// <summary>Get the spatial blend for a given speak mode (1 = 3D, 0 = 2D).</summary>
		public float GetSpatialBlend(SpeakMode mode) {
			return mode switch {
				SpeakMode.Whisper   => WhisperSpatialBlend,
				SpeakMode.Normal    => NormalSpatialBlend,
				SpeakMode.Loud      => LoudSpatialBlend,
				SpeakMode.Broadcast => BroadcastSpatialBlend,
				_                   => NormalSpatialBlend
			};
		}

		/// <summary>Get the audio rolloff mode for a given speak mode.</summary>
		public AudioRolloffMode GetRolloff(SpeakMode mode) {
			return mode switch {
				SpeakMode.Whisper   => WhisperRolloff,
				SpeakMode.Normal    => NormalRolloff,
				SpeakMode.Loud      => LoudRolloff,
				SpeakMode.Broadcast => AudioRolloffMode.Linear,
				_                   => NormalRolloff
			};
		}
	}
}
