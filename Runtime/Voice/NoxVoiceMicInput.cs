using System;
using UnityEngine;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>
	/// Microphone-based voice input — MetaVoiceChat VcMicAudioInput equivalent.
	/// Wraps Unity's Microphone API with configurable sample rate and frame size.
	/// </summary>
	public class NoxVoiceMicInput : NoxVoiceInput {
		[Tooltip("Microphone device name (null = default device).")]
		public string DeviceName;

		[Tooltip("Sample rate for microphone capture.")]
		public int SampleRate = 48000;

		private AudioClip _micClip;
		private int _lastPosition;
		private int _frameIndex;
		private float[] _frameBuffer;
		private bool _isRecording;

		public override void StartLocalPlayer() {
			if (_isRecording) return;

			var config = VoiceChat.Config;
			_frameBuffer = new float[config.SamplesPerFrame];

			// Start Unity microphone capture
			_micClip = UnityEngine.Microphone.Start(DeviceName, loop: true, lengthSec: 1, frequency: SampleRate);
			if (_micClip == null) {
				Debug.LogError($"[NoxVoiceMicInput] Failed to start microphone '{DeviceName}'");
				return;
			}

			_lastPosition = 0;
			_frameIndex = 0;
			_isRecording = true;
		}

		private void Update() {
			if (!_isRecording || _micClip == null) return;

			int pos = UnityEngine.Microphone.GetPosition(DeviceName);
			if (pos == _lastPosition) return;

			int samplesAvailable;
			if (pos > _lastPosition)
				samplesAvailable = pos - _lastPosition;
			else
				samplesAvailable = (_micClip.samples - _lastPosition) + pos;

			var config = VoiceChat.Config;
			int frameSize = config.SamplesPerFrame;

			while (samplesAvailable >= frameSize) {
				// Read one frame from the ring buffer
				int start = _lastPosition % _micClip.samples;
				if (start + frameSize <= _micClip.samples) {
					_micClip.GetData(_frameBuffer, start);
				} else {
					// Wraparound
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

				// Pass through filter chain and fire event
				float[] frameCopy = new float[frameSize];
				Array.Copy(_frameBuffer, frameCopy, frameSize);
				SendAndFilterFrame(_frameIndex++, frameCopy);
			}
		}

		private void OnDestroy() {
			if (_isRecording) {
				UnityEngine.Microphone.End(DeviceName);
				_isRecording = false;
			}
		}
	}
}
