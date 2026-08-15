using System;
using UnityEngine;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>
	/// Voice chat configuration — MetaVoiceChat-style VcConfig equivalent.
	/// Created as a ScriptableObject asset for editor tuning.
	/// </summary>
	[CreateAssetMenu(fileName = "NoxVoiceConfig", menuName = "Nox/Voice Config")]
	public class NoxVoiceConfig : ScriptableObject {
		// ── Constants (matching Opus specs) ──
		public const int BitsPerSample = 16;
		public const int SamplesPerSecond = 48_000;
		public const int ClipLoopSeconds = 1;
		public const int SamplesPerClip = SamplesPerSecond * ClipLoopSeconds;

		[Header("Opus Codec")]
		[Tooltip("Opus complexity: 0=fast/low quality, 10=slow/high quality. Default 10.")]
		[Range(0, 10)]
		public int Complexity = 10;

		[Tooltip("Opus frame duration in ms. Valid: 10, 20, 40. Default 10ms.")]
		public OpusFrameSize FrameSize = OpusFrameSize.Ms10;

		[Tooltip("Opus target bitrate in bps.")]
		public int Bitrate = 32000;

		[Header("Jitter Buffer")]
		[Tooltip("RMS jitter calculation window in seconds.")]
		[Range(0.040f, 1.200f)]
		public float JitterTimeWindow = 0.240f;

		[Tooltip("Number of updates for mean offset calculation.")]
		[Range(10, 1000)]
		public int JitterMeanOffsetWindow = 100;

		[Tooltip("Minimum fractional frames between buffer read/write. Lower = less latency but riskier.")]
		[Range(1, 10)]
		public float OutputMinBufferFrames = 2.0f;

		[Header("Pitch Compensation")]
		[Tooltip("P-controller proportional gain (0 = disabled). Lower = smoother, less warbly.")]
		[Range(0, 10)]
		public float PitchProportionalGain = 0.3f;

		[Tooltip("Maximum pitch correction as fraction (±). Lower = less warbly artifact.")]
		[Range(0, 0.5f)]
		public float PitchMaxCorrection = 0.05f;

		[Header("AudioSource Output")]
		[Tooltip("Maximum frame lifetime in the buffer before clearing.")]
		[Range(0.1f, 0.75f)]
		public float FrameLifetime = 0.5f;

		[Tooltip("Largest negative latency before wrapping. Used for AudioClip circular buffer.")]
		[Range(0.1f, 0.5f)]
		public float MaxNegativeLatency = 0.25f;

		[Header("3D Spatial")]
		[Tooltip("Default distance mode for outgoing voice. 0=Normal, 1=Whisper, 2=Broadcast.")]
		public VoiceDistanceMode DefaultDistanceMode = VoiceDistanceMode.Normal;

		[Tooltip("Minimum distance for 3D audio (full volume).")]
		[Range(0f, 50f)]
		public float SpatialMinDistance = 1f;

		[Tooltip("Maximum distance for 3D audio (silence). Normal mode.")]
		[Range(1f, 500f)]
		public float SpatialMaxDistanceNormal = 40f;

		[Tooltip("Maximum distance for whisper mode.")]
		[Range(0.5f, 20f)]
		public float SpatialMaxDistanceWhisper = 5f;

		[Tooltip("Volume rolloff curve for 3D audio.")]
		public AudioRolloffMode SpatialRolloff = AudioRolloffMode.Logarithmic;

		// ── Derived values (set by Init()) ──
		[NonSerialized] public int FramePeriodMs;
		[NonSerialized] public int FramesPerSecond;
		[NonSerialized] public float SecondsPerFrame;
		[NonSerialized] public int SamplesPerFrame;
		[NonSerialized] public int FramesPerClip;

		public void Init() {
			FramePeriodMs = FrameSize switch {
				OpusFrameSize.Ms10 => 10,
				OpusFrameSize.Ms20 => 20,
				OpusFrameSize.Ms40 => 40,
				_ => 20
			};

			FramesPerSecond = 1000 / FramePeriodMs;
			SecondsPerFrame = FramePeriodMs / 1000f;
			SamplesPerFrame = SamplesPerSecond / FramesPerSecond;
			FramesPerClip = FramesPerSecond * ClipLoopSeconds;
		}
	}

	/// <summary>Opus frame duration.</summary>
	public enum OpusFrameSize {
		Ms10 = 10,
		Ms20 = 20,
		Ms40 = 40
	}
}
