using System;
using UnityEngine;
using AudioMicrophone = Nox.Audio.Runtime.Microphone.Microphone;
using AudioMicrophoneManager = Nox.Audio.Runtime.Microphone.MicrophoneManager;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>
	/// Microphone-based voice input — reads the shared microphone stream from
	/// nox.audio's MicrophoneManager, applies the DSP (volume, noise suppression,
	/// activation gate) via the microphone, then emits processed frames.
	/// </summary>
	public class VoiceMicInput : MonoBehaviour {
		public VoiceChat VoiceChat;

		/// <summary>Fired when a new (processed) audio frame is ready: (frameIndex, pcmSamples).</summary>
		public event Action<int, float[]> OnFrameReady;

		private AudioMicrophoneManager _manager;
		private AudioMicrophone _mic;

		private AudioClip _micClip;
		private int _lastPosition;
		private int _frameIndex;
		private float[] _frameBuffer;
		private bool _isRecording;

		public void StartLocalPlayer() {
			if (_isRecording) return;

			var config = VoiceChat.Config;
			_frameBuffer = new float[config.SamplesPerFrame];

			_manager = Nox.Audio.Runtime.Main.MicrophoneManager;
			if (_manager == null) {
				Debug.LogError("[VoiceMicInput] nox.audio MicrophoneManager is unavailable.");
				return;
			}

			// Follow device changes.
			_manager.OnCurrentChanged.AddListener(OnCurrentMicChanged);

			if (!BeginCapture(_manager.Current))
				return;

			_frameIndex = 0;
			_isRecording = true;
		}

		/// <summary>Start (or switch) capture on the given microphone.</summary>
		private bool BeginCapture(AudioMicrophone mic) {
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
			return true;
		}

		private void OnCurrentMicChanged(AudioMicrophone arg0) {
			if (!_isRecording) return;

			// Release the previous device and switch to the new current microphone.
			_mic?.Stop("voice");
			BeginCapture(_manager.Current);
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

			var config = VoiceChat.Config;
			int frameSize = config.SamplesPerFrame;

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

				// Apply nox.audio DSP (volume, noise suppression, activation gate).
				_mic.Process(frameCopy);

				OnFrameReady?.Invoke(_frameIndex++, frameCopy);
			}
		}

		private void OnDestroy() {
			if (_isRecording) {
				_mic?.Stop("voice");
				_isRecording = false;
			}

			if (_manager != null)
				_manager.OnCurrentChanged.RemoveListener(OnCurrentMicChanged);
			_manager = null;
		}
	}
}
