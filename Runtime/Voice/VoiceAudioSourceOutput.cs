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

		[Header("Level")]
		[Tooltip("Gain applied to the RMS of the decoded frames to get the 0-1 level exposed to UI " +
		         "(same convention as the microphone loudness: rms * gain).")]
		[Range(1f, 50f)]
		public float LevelGain = 10f;

		/// <summary>
		/// Level of the received voice in the <c>0-1</c> range (silence to full scale), for UI
		/// indicators (speaking ring, nameplate voice image, ...). 
		/// </summary>
		public float Level { get; private set; }

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

		/// <summary>
		/// Time since the last frame that carried <b>actual samples</b>. A silence frame (the player
		/// is muted, or the sender only sends keep-alives) refreshes <see cref="TimeSincePreviousFrame"/>
		/// but must not keep the buffered tail alive.
		/// </summary>
		private readonly System.Diagnostics.Stopwatch _signalStopwatch = new();
		private float TimeSinceLastSignal => (float)_signalStopwatch.Elapsed.TotalSeconds;

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

			// Same for the buffer tuning: without this the serialized defaults are used and
			// tuning VoiceConfig has no effect on the stale-frame timeout.
			FrameLifetime = VoiceConfig.FrameLifetime;
			MaxNegativeLatency = VoiceConfig.MaxNegativeLatency;

			_vcAudioClip = new VoiceAudioClip(AudioSource);
			_clipFrameIndices = new int[OpusConfig.FramesPerClip];
			for (int i = 0; i < _clipFrameIndices.Length; i++)
				_clipFrameIndices[i] = -1;
		}

		public void Update() {
			// No real (non-silent) frame for a while: the sender stopped streaming (it does not send
			// silence — see LocalVoiceProvider) or the player got muted. The clip loops and already
			// holds the tail, so without this the last packets would be replayed indefinitely.
			if (_isInit && TimeSinceLastSignal > FrameLifetime)
				Silence();

			// Wait until buffer is built up to target latency before starting playback
			if (!_isInit) {
				int receivedFrames = _greatestFrameIndex == -1
					? 0
					: _greatestFrameIndex - _firstFrameIndex + 1;

				// Only prime on frames that carry audio: silence frames must not start a playback
				// that would be cut again on the next Update.
				if (receivedFrames != 0 && TimeSinceLastSignal <= FrameLifetime) {
					float timeSinceFirstFrame = ((float)receivedFrames / _framesPerSecond) + TimeSincePreviousFrame;
					if (timeSinceFirstFrame >= _targetLatency) {
						AudioSource.time = GetWrappedTime(_firstFrameIndex);
						AudioSource.Play();
						_isInit = true;
					}
				}

				if (!_isInit) return;
			}

			// The source can be stopped behind our back: a hidden GameObject (the physical, while the
			// player is out of range) or a hidden avatar providing the AudioSource (see SetSource)
			// stops it, and a deactivated AudioSource never resumes on its own.
			if (!AudioSource.isPlaying)
				ResumePlayback();

			// ── Pitch compensation P-controller ──
			float latency = GetLatency();
			float error = _targetLatency - latency;
			float response = -error * PitchProportionalGain;
			response = Mathf.Clamp(response, -PitchMaxCorrection, PitchMaxCorrection);
			AudioSource.pitch = 1f + response;

			ClearOldFrames();
		}

		/// <summary>
		/// Restarts the playback after the <see cref="AudioSource"/> was stopped behind our back (the
		/// physical or the avatar was hidden). The read head is re-synced on the newest buffered frame
		/// minus the target latency: resuming from the previous position would leave the latency far
		/// from the target, which the pitch controller only absorbs at ~1%/s — i.e. seconds of
		/// stretched, crackling audio.
		/// </summary>
		/// <summary>
		/// Drops the buffered audio, resets the read/write state and stops the source.
		/// <para>
		/// Called when the input goes silent (<see cref="Update"/>) and when the player is muted
		/// (<c>RemoteVoiceProvider</c>) so that the looping clip can never keep replaying the last
		/// received packets. Playback restarts on the next frames carrying audio.
		/// </para>
		/// </summary>
		public void Silence() {
			_vcAudioClip?.Clear();

			_isInit            = false;
			_firstFrameIndex   = -1;
			_greatestFrameIndex = -1;
			Level              = 0f;

			if (_clipFrameIndices != null) {
				for (int i = 0; i < _clipFrameIndices.Length; i++)
					_clipFrameIndices[i] = -1;
			}

			if (AudioSource != null) {
				AudioSource.pitch = 1f;
				if (AudioSource.isPlaying)
					AudioSource.Stop();
			}
		}

		private void ResumePlayback() {
			if (_greatestFrameIndex >= 0) {
				float readTime = GetWrappedTime(_greatestFrameIndex) - _targetLatency;
				AudioSource.time = readTime < 0f 
					? readTime + _vcAudioClip.Length 
					: readTime;
			}

			AudioSource.Play();
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
			Level = ComputeLevel(samples);

			if (HasSignal(samples))
				_signalStopwatch.Restart();

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
		/// Whether a frame carries actual samples (same gate as the sender's silence optimization):
		/// a muted/empty frame is not a reason to keep the buffered audio playing.
		/// </summary>
		private static bool HasSignal(float[] samples) {
			if (samples == null || samples.Length == 0)
				return false;

			float sumSq = 0f;
			for (int i = 0; i < samples.Length; i++)
				sumSq += samples[i] * samples[i];

			return sumSq > 1e-6f;
		}

		/// <summary>
		/// Level of a decoded frame in the <c>0-1</c> range: its RMS scaled by <see cref="LevelGain"/>
		/// (same convention as <c>IMicrophone.Loudness</c>). A missing or empty frame is silence.
		/// </summary>
		private float ComputeLevel(float[] samples) {
			if (samples == null || samples.Length == 0)
				return 0f;

			float sumSq = 0f;
			for (int i = 0; i < samples.Length; i++)
				sumSq += samples[i] * samples[i];

			return Mathf.Clamp01(Mathf.Sqrt(sumSq / samples.Length) * LevelGain);
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
