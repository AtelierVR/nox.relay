using System;
using UnityEngine;
using UnityEngine.Audio;
using Nox.CCK.Audio.Opus;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>
	/// AudioSource-based voice output with jitter buffer and pitch compensation.
	/// MetaVoiceChat VcAudioSourceOutput equivalent.
	/// Uses a circular AudioClip for zero-copy frame writing.
	/// </summary>
	public class VoiceAudioSourceOutput : MonoBehaviour {
		[Tooltip("The output AudioSource.")]
		public AudioSource AudioSource;

		[Tooltip("Optional mixer group to route this output through (e.g. a voice track).")]
		public AudioMixerGroup MixerGroup;

		[Header("Buffer Tuning")]
		[Tooltip("Frame lifetime in the buffer before clearing. Units: seconds.")]
		[Range(0.1f, 0.75f)]
		public float FrameLifetime = 0.5f;

		[Tooltip("Largest negative latency before wrapping to positive.")]
		[Range(0.1f, 0.5f)]
		public float MaxNegativeLatency = 0.25f;

		[Header("Pitch P-Controller")]
		[Tooltip("Proportional gain in % per second of latency error. Lower = smoother.")]
		[Range(0, 10)]
		public float PitchProportionalGain = 0.05f;

		[Tooltip("Maximum pitch correction (fraction). Lower = less warbly.")]
		[Range(0, 0.5f)]
		public float PitchMaxCorrection = 0.01f;

		[Header("3D Spatial")]
		[Tooltip("Current distance mode for this output.")]
		public VoiceDistanceMode DistanceMode = VoiceDistanceMode.Normal;

		/// <summary>
		/// Apply 3D spatial settings based on the current distance mode.
		/// Call when the mode changes or after SetSource.
		/// </summary>
		public void ApplySpatialSettings() {
			if (AudioSource == null) return;

			switch (DistanceMode) {
				case VoiceDistanceMode.Normal:
					AudioSource.spatialBlend = 1f;
					AudioSource.minDistance = VoiceConfig.SpatialMinDistance;
					AudioSource.maxDistance = VoiceConfig.SpatialMaxDistanceNormal;
					AudioSource.rolloffMode = VoiceConfig.SpatialRolloff;
					break;
				case VoiceDistanceMode.Whisper:
					AudioSource.spatialBlend = 1f;
					AudioSource.minDistance = VoiceConfig.SpatialMinDistance * 0.5f;
					AudioSource.maxDistance = VoiceConfig.SpatialMaxDistanceWhisper;
					AudioSource.rolloffMode = VoiceConfig.SpatialRolloff;
					break;
				case VoiceDistanceMode.Broadcast:
					AudioSource.spatialBlend = 0f; // 2D global
					AudioSource.rolloffMode = AudioRolloffMode.Custom;
					AudioSource.SetCustomCurve(
						AudioSourceCurveType.CustomRolloff,
						AnimationCurve.Constant(0f, 1f, 1f)
					); // flat volume
					break;
			}
		}

		private int _framesPerSecond;
		private float _secondsPerFrame;

		private VoiceAudioClip _vcAudioClip;
		private int[] _clipFrameIndices;

		private int _firstFrameIndex = -1;
		private int _greatestFrameIndex = -1;

		private readonly System.Diagnostics.Stopwatch _frameStopwatch = new();
		private float TimeSincePreviousFrame => (float)_frameStopwatch.Elapsed.TotalSeconds;

		private bool _isInit;
		private float _targetLatency;

		private void Start() {
			if (AudioSource == null) {
				AudioSource = GetComponent<AudioSource>();
				if (AudioSource == null)
					AudioSource = gameObject.AddComponent<AudioSource>();
			}

			AudioSource.dopplerLevel = 0; // Doppler interferes with pitch compensation
			AudioSource.outputAudioMixerGroup = MixerGroup;

			ApplySpatialSettings();

			_framesPerSecond = OpusConfig.FramesPerSecond;
			_secondsPerFrame = OpusConfig.SecondsPerFrame;

			// Apply config pitch-compensation settings (otherwise the serialized field
			// defaults are used and tuning VoiceConfig has no effect on the wobble).
			PitchProportionalGain = VoiceConfig.PitchProportionalGain;
			PitchMaxCorrection = VoiceConfig.PitchMaxCorrection;

			_vcAudioClip = new VoiceAudioClip(AudioSource);
			_clipFrameIndices = new int[OpusConfig.FramesPerClip];
			for (int i = 0; i < _clipFrameIndices.Length; i++)
				_clipFrameIndices[i] = -1;
		}

		public void Update() {
			// Clear stale data when no frames received for a while.
			// Reset init state so playback can restart when frames resume.
			if (_isInit && TimeSincePreviousFrame > FrameLifetime) {
				_vcAudioClip.Clear();
				_isInit = false;
				_firstFrameIndex = -1;
				_greatestFrameIndex = -1;
				for (int i = 0; i < _clipFrameIndices.Length; i++)
					_clipFrameIndices[i] = -1;
			}

			// Wait until buffer is built up to target latency before starting playback
			if (!_isInit) {
				int receivedFrames = _greatestFrameIndex == -1
					? 0
					: _greatestFrameIndex - _firstFrameIndex + 1;

				if (receivedFrames != 0) {
					float timeSinceFirstFrame = ((float)receivedFrames / _framesPerSecond) + TimeSincePreviousFrame;
					if (timeSinceFirstFrame >= _targetLatency) {
						AudioSource.time = GetWrappedTime(_firstFrameIndex);
						AudioSource.Play();
						_isInit = true;
					}
				}

				if (!_isInit) return;
			}

			// ── Pitch compensation P-controller ──
			float latency = GetLatency();
			float error = _targetLatency - latency;
			float response = -error * PitchProportionalGain;
			response = Mathf.Clamp(response, -PitchMaxCorrection, PitchMaxCorrection);
			AudioSource.pitch = 1f + response;

			ClearOldFrames();
		}

		private void ClearOldFrames() {
			for (int i = 0; i < _clipFrameIndices.Length; i++) {
				int frameIndex = _clipFrameIndices[i];
				if (frameIndex != -1) {
					int ageFrames = _greatestFrameIndex - frameIndex;
					float ageSeconds = ageFrames * _secondsPerFrame;
					if (ageSeconds > FrameLifetime) {
						_vcAudioClip.ClearFrame(i);
						_clipFrameIndices[i] = -1;
					}
				}
			}
		}

		private float GetLatency()
			=> GetRawLatency() + TimeSincePreviousFrame;

		private float GetRawLatency() {
			float writeTime = GetWrappedTime(_greatestFrameIndex);
			float readTime = AudioSource.time;
			float latency = writeTime - readTime;
			float clipLength = _vcAudioClip.Length;

			if (latency < 0)
				latency = clipLength + latency;

			if (clipLength - MaxNegativeLatency < latency)
				latency -= clipLength;

			return latency;
		}

		private float GetWrappedTime(int frameIndex)
			=> _vcAudioClip.GetOffsetFrames(frameIndex) * _secondsPerFrame;

		public void ReceiveFrame(int index, float[] samples, float targetLatency) {
			if (_vcAudioClip == null) return;

			_targetLatency = targetLatency;

			int offsetFrames = _vcAudioClip.GetOffsetFrames(index);
			_vcAudioClip.WriteFrame(offsetFrames, samples);
			_clipFrameIndices[offsetFrames] = index;

			if (_firstFrameIndex == -1)
				_firstFrameIndex = index;

			if (index > _greatestFrameIndex)
				_greatestFrameIndex = index;

			_frameStopwatch.Restart();
		}

		private void OnDestroy() {
			_vcAudioClip?.Dispose();
		}

		/// <summary>
		/// Migrate to a different AudioSource (e.g. from VoiceAvatarModule).
		/// Recreates the circular clip on the new source.
		/// </summary>
		public void SetSource(AudioSource newSource) {
			if (newSource == null || newSource == AudioSource) return;

			bool wasPlaying = AudioSource != null && AudioSource.isPlaying;
			if (AudioSource != null) {
				AudioSource.Stop();
				AudioSource.clip = null;
			}

			AudioSource = newSource;
			AudioSource.dopplerLevel = 0;
			AudioSource.playOnAwake = false;
			AudioSource.loop = true;
			AudioSource.outputAudioMixerGroup = MixerGroup;

			ApplySpatialSettings();

			// Recreate the circular clip on the new AudioSource
			_vcAudioClip?.Dispose();

			// Ensure fields are initialized (may be called before Start())
			if (_framesPerSecond == 0) {
				_framesPerSecond = OpusConfig.FramesPerSecond;
				_secondsPerFrame = OpusConfig.SecondsPerFrame;
			}

			_vcAudioClip = new VoiceAudioClip(AudioSource);

			// Reset frame tracking (handle uninitialized array from pre-Start call)
			if (_clipFrameIndices == null || _clipFrameIndices.Length != OpusConfig.FramesPerClip)
				_clipFrameIndices = new int[OpusConfig.FramesPerClip];
			for (int i = 0; i < _clipFrameIndices.Length; i++)
				_clipFrameIndices[i] = -1;
			_firstFrameIndex = -1;
			_greatestFrameIndex = -1;

			// Reset init so Update() re-buffers and calls Play() at the correct time.
			// Don't call Play() here — let the buffer fill to target latency first.
			if (_isInit) {
				_isInit = false;
				_frameStopwatch.Restart();
			}
		}
	}

	/// <summary>
	/// Circular AudioClip for voice playback — manages a looping AudioClip with
	/// frame-indexed write positions.
	/// </summary>
	public class VoiceAudioClip : IDisposable {
		private readonly int _samplesPerFrame;
		private readonly int _framesPerClip;
		private readonly AudioClip _audioClip;

		private readonly float[] _emptyFrame;
		private readonly float[] _emptyClip;

		public float Length => _audioClip.length;

		public VoiceAudioClip(AudioSource audioSource) {
			_samplesPerFrame = OpusConfig.SamplesPerFrame;
			_framesPerClip = OpusConfig.FramesPerClip;

			_audioClip = AudioClip.Create(nameof(VoiceAudioClip),
				OpusConfig.SamplesPerClip, channels: 1,
				OpusConfig.SamplesPerSecond, stream: false);

			_emptyFrame = new float[_samplesPerFrame];
			_emptyClip = new float[OpusConfig.SamplesPerClip];

			audioSource.playOnAwake = false;
			audioSource.Stop();
			audioSource.loop = true;
			audioSource.clip = _audioClip;
		}

		/// <summary>Write a frame of PCM samples at the given frame offset.</summary>
		public void WriteFrame(int offsetFrames, float[] samples) {
			samples ??= _emptyFrame;

			if (samples.Length != _samplesPerFrame) {
				Debug.LogWarning("[VoiceAudioClip] Sample count mismatch with config!");
				return;
			}

			int offsetSamples = _samplesPerFrame * offsetFrames;
			_audioClip.SetData(samples, offsetSamples);
		}

		/// <summary>Get the circular frame offset for a monotonic frame index.</summary>
		public int GetOffsetFrames(int frameIndex)
			=> frameIndex % _framesPerClip;

		/// <summary>Clear a single frame (set to silence).</summary>
		public void ClearFrame(int offsetFrames) {
			_audioClip.SetData(_emptyFrame, _samplesPerFrame * offsetFrames);
		}

		/// <summary>Clear the entire clip.</summary>
		public void Clear() {
			_audioClip.SetData(_emptyClip, 0);
		}

		public void Dispose() {
			UnityEngine.Object.Destroy(_audioClip);
		}
	}
}
