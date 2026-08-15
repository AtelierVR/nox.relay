using System;
using UnityEngine;
using AudioMicrophone = Nox.Audio.Runtime.Microphone.Microphone;
using AudioMicrophoneManager = Nox.Audio.Runtime.Microphone.MicrophoneManager;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>
	/// Microphone-based voice input — reads the shared microphone stream from
	/// nox.audio's MicrophoneManager. Noise suppression and activation gate are handled
	/// by <see cref="MicrophoneProcessor"/> (in nox.audio) via the
	/// <see cref="NoxVoiceInput.OptionalFirstInputFilter"/> chain.
	/// </summary>
	public class NoxVoiceMicInput : NoxVoiceInput {
		[Tooltip("Microphone device name (null = current device).")]
		public string DeviceName;

		private AudioMicrophoneManager _manager;
		private AudioMicrophone _mic;

		private AudioClip _micClip;
		private int _lastPosition;
		private int _frameIndex;
		private float[] _frameBuffer;
		private bool _isRecording;

		public override void StartLocalPlayer() {
			if (_isRecording) return;

			var config = VoiceChat.Config;
			_frameBuffer = new float[config.SamplesPerFrame];

			_manager = Nox.Audio.Runtime.Main.MicrophoneManager;
			if (_manager == null) {
				Debug.LogError("[NoxVoiceMicInput] nox.audio MicrophoneManager is unavailable.");
				return;
			}

			// Follow device changes when using the "current" microphone.
			if (string.IsNullOrEmpty(DeviceName))
				_manager.OnCurrentChanged.AddListener(OnCurrentMicChanged);

			if (!BeginCapture(ResolveMic()))
				return;

			_frameIndex = 0;
			_isRecording = true;
		}

		private AudioMicrophone ResolveMic()
			=> string.IsNullOrEmpty(DeviceName)
				? _manager?.Current
				: _manager?.Microphones.Find(m => m.Name == DeviceName);

		/// <summary>Start (or switch) capture on the given microphone.</summary>
		private bool BeginCapture(AudioMicrophone mic) {
			if (mic == null) {
				Debug.LogError("[NoxVoiceMicInput] No microphone available via nox.audio.");
				return false;
			}

			var clip = mic.Start("voice");
			if (clip == null) {
				Debug.LogError($"[NoxVoiceMicInput] Failed to start microphone '{mic.Name}'");
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

				SendAndFilterFrame(_frameIndex++, frameCopy);
			}
		}

		private void OnDestroy() {
			if (_isRecording) {
				_mic?.Stop("voice");
				_isRecording = false;
			}

			if (_manager != null && string.IsNullOrEmpty(DeviceName))
				_manager.OnCurrentChanged.RemoveListener(OnCurrentMicChanged);
			_manager = null;
		}
	}
}
