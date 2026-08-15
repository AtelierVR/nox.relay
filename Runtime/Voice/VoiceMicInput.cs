using System;
using UnityEngine;
using Nox.Audio;
using Nox.CCK.Audio.Opus;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>
	/// Microphone-based voice input — reads the shared microphone stream from
	/// nox.audio's MicrophoneManager, applies the DSP (volume, noise suppression,
	/// activation gate) via the microphone, then emits processed frames.
	/// </summary>
	public class VoiceMicInput : MonoBehaviour {
		/// <summary>Fired when a new (processed) audio frame is ready: (frameIndex, pcmSamples).</summary>
		public event Action<int, float[]> OnFrameReady;

		private IMicrophone _mic;

		private AudioClip _micClip;
		private int _lastPosition;
		private int _frameIndex;
		private float[] _frameBuffer;
		private bool _isRecording;

		public void StartLocalPlayer() {
			if (_isRecording) return;

			_frameBuffer = new float[OpusConfig.SamplesPerFrame];

			if (Main.MicrophoneAPI == null) {
				Debug.LogError("[VoiceMicInput] nox.audio MicrophoneManager is unavailable.");
				return;
			}

			if (!BeginCapture(Main.MicrophoneAPI.Current))
				return;

			_frameIndex = 0;
			_isRecording = true;
		}

		/// <summary>Start (or switch) capture on the given microphone.</summary>
		private bool BeginCapture(IMicrophone mic) {
			if (mic == null) {
				Debug.LogError("[VoiceMicInput] No microphone available via nox.audio.");
				return false;
			}

			var clip = mic.Start("voice");
			if (clip == null) {
				Debug.LogError($"[VoiceMicInput] Failed to start microphone '{mic.Name}'");
				return false;
			}

			_mic         = mic;
			_micClip     = clip;
			// Start from the current write position: the clip is a shared ring buffer
			// that may already be full (e.g. started by MicrophoneManager's "current"
			// user). Reading from 0 would replay seconds of stale audio.
			_lastPosition = mic.Position;

			// Diagnostic: the whole pipeline assumes 48 kHz. If the device records at
			// a different rate (e.g. 44100), each frame is slightly off → pitch shift +
			// periodic buffer underrun (audible "grésillement").
			if (clip.frequency != OpusConfig.SamplesPerSecond)
				Debug.LogWarning($"[VoiceMicInput] Microphone '{mic.Name}' records at {clip.frequency} Hz (expected {OpusConfig.SamplesPerSecond}). Resampling required.");
			return true;
		}

		private void Update() {
			if (!_isRecording || _mic == null || _micClip == null) return;

			int pos = _mic.Position;
			if (pos < 0) return;
			pos %= _micClip.samples;
			if (pos == _lastPosition) return;

			int samplesAvailable;
			if (pos > _lastPosition)
				samplesAvailable = pos - _lastPosition;
			else
				samplesAvailable = (_micClip.samples - _lastPosition) + pos;

			int frameSize = OpusConfig.SamplesPerFrame;
			if (frameSize <= 0)
				return;

			while (samplesAvailable >= frameSize) {
				int start = _lastPosition % _micClip.samples;
				if (start + frameSize <= _micClip.samples) {
					_micClip.GetData(_frameBuffer, start);
				} else {
					int first = _micClip.samples - start;
					int second = frameSize - first;
					var temp = new float[first];
					_micClip.GetData(temp, start);
					Array.Copy(temp, 0, _frameBuffer, 0, first);
					var tail = new float[second];
					_micClip.GetData(tail, 0);
					Array.Copy(tail, 0, _frameBuffer, first, second);
				}

				_lastPosition = (start + frameSize) % _micClip.samples;
				samplesAvailable -= frameSize;

				float[] frameCopy = new float[frameSize];
				Array.Copy(_frameBuffer, frameCopy, frameSize);

				// The microphone's ClipProcessor (in nox.audio) normally mutes the clip
				// in place, but it runs on a different update (OnUpdateMain vs. this
				// MonoBehaviour.Update), so it can race with this reader. Enforce mute
				// here as an authoritative, idempotent boundary check so no sound ever
				// leaks while muted.
				if (_mic.IsMuted)
					Array.Clear(frameCopy, 0, frameCopy.Length);

				OnFrameReady?.Invoke(_frameIndex++, frameCopy);
			}
		}

		private void OnDestroy() {
			if (_isRecording) {
				_mic?.Stop("voice");
				_isRecording = false;
			}
		}
	}
}
