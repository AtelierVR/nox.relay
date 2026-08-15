using UnityEngine;
using Nox.CCK.Utils;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>
	/// Voice chat configuration — static accessor over the shared config store
	/// (<see cref="Config.Load()"/>). Values are read live from config.json with
	/// sensible defaults, so no ScriptableObject asset is required.
	/// </summary>
	public static class VoiceConfig {
		private const string Prefix = "settings.relay.voice";

		private static T Get<T>(string key, T fallback)
			=> Config.Load().Get($"{Prefix}.{key}", fallback);

		// ── Jitter Buffer ──
		public static float JitterTimeWindow 
			=> Get("jitter_time_window", 0.240f);
		public static int JitterMeanOffsetWindow 
			=> Get("jitter_mean_offset_window", 100);
		public static float OutputMinBufferFrames 
			=> Get("output_min_buffer_frames", 2.0f);

		// ── Pitch Compensation ──
		public static float PitchProportionalGain 
			=> Get("pitch_proportional_gain", 0.05f);
		public static float PitchMaxCorrection 
			=> Get("pitch_max_correction", 0.01f);

		// ── AudioSource Output ──
		public static float FrameLifetime 
			=> Get("frame_lifetime", 0.5f);
		public static float MaxNegativeLatency 
			=> Get("max_negative_latency", 0.25f);

		// ── 3D Spatial ──
		public static VoiceDistanceMode DefaultDistanceMode 
			=> Get("default_distance_mode", VoiceDistanceMode.Normal);
		public static float SpatialMinDistance 
			=> Get("spatial_min_distance", 1f);
		public static float SpatialMaxDistanceNormal 
			=> Get("spatial_max_distance_normal", 40f);
		public static float SpatialMaxDistanceWhisper 
			=> Get("spatial_max_distance_whisper", 5f);
		public static AudioRolloffMode SpatialRolloff 
			=> Get("spatial_rolloff", AudioRolloffMode.Logarithmic);
	}
}
