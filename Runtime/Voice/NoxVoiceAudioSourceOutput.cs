using System;
using UnityEngine;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>
	/// AudioSource-based voice output with jitter buffer and pitch compensation.
	/// MetaVoiceChat VcAudioSourceOutput equivalent.
	/// Uses a circular AudioClip for zero-copy frame writing.
	/// </summary>
	public class NoxVoiceAudioSourceOutput : NoxVoiceOutput {
		[Tooltip("The output AudioSource.")]
		public AudioSource AudioSource;

		[Header("Buffer Tuning")]
		[Tooltip("Frame lifetime in the buffer before clearing. Units: seconds.")]
		[Range(0.1f, 0.75f)]
		public float FrameLifetime = 0.5f;

		[Tooltip("Largest negative latency before wrapping to positive.")]
		[Range(0.1f, 0.5f)]
		public float MaxNegativeLatency = 0.25f;

		[Header("Pitch P-Controller")]
		[Tooltip("Proportional gain in % per second of latency error.")]
		[Range(0, 10)]
		public float PitchProportionalGain = 1f;

		[Tooltip("Maximum pitch correction (fraction).")]
		[Range(0, 0.5f)]
		public float PitchMaxCorrection = 0.2f;

		private int _framesPerSecond;
		private float _secondsPerFrame;

		private NoxVoiceAudioClip _vcAudioClip;
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

			var config = VoiceChat?.Config;
			if (config == null) {
				Debug.LogWarning("[NoxVoiceAudioSourceOutput] No config on VoiceChat, using defaults");
				return;
			}

			config.Init();
			_framesPerSecond = config.FramesPerSecond;
			_secondsPerFrame = config.SecondsPerFrame;

			_vcAudioClip = new NoxVoiceAudioClip(config, AudioSource);
			_clipFrameIndices = new int[config.FramesPerClip];
			for (int i = 0; i < _clipFrameIndices.Length; i++)
				_clipFrameIndices[i] = -1;
		}

		private void Update() {
			// Clear stale data when no frames received for a while
			if (_isInit && TimeSincePreviousFrame > FrameLifetime)
				_vcAudioClip.Clear();

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

		protected override void ReceiveFrame(int index, float[] samples, float targetLatency) {
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

			// Recreate the circular clip on the new AudioSource
			_vcAudioClip?.Dispose();
			var config = VoiceChat?.Config;
			if (config != null) {
				config.Init();

				// Ensure fields are initialized (may be called before Start())
				if (_framesPerSecond == 0) {
					_framesPerSecond = config.FramesPerSecond;
					_secondsPerFrame = config.SecondsPerFrame;
				}

				_vcAudioClip = new NoxVoiceAudioClip(config, AudioSource);

				// Reset frame tracking (handle uninitialized array from pre-Start call)
				if (_clipFrameIndices == null || _clipFrameIndices.Length != config.FramesPerClip)
					_clipFrameIndices = new int[config.FramesPerClip];
				for (int i = 0; i < _clipFrameIndices.Length; i++)
					_clipFrameIndices[i] = -1;
				_firstFrameIndex = -1;
				_greatestFrameIndex = -1;
			}

			if (wasPlaying)
				AudioSource.Play();
		}
	}
}
