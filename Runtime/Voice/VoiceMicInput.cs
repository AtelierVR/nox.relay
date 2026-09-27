using System;
using UnityEngine;
using Nox.Audio;
using Nox.CCK.Audio.Opus;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>
	/// Microphone-based voice input — consumes the DSP-processed frames published by
	/// nox.audio's microphone (<c>volume</c>, noise suppression and activation gate already
	/// applied, on the DSP's own frame grid) and hands them to the Opus encoder.
	/// </summary>
	public class VoiceMicInput : MonoBehaviour {
		/// <summary>Fired when a new (processed) audio frame is ready: (frameIndex, pcmSamples).</summary>
		public event Action<int, float[]> OnFrameReady;

		private IMicrophone _mic;

		private int  _frameIndex;
		private bool _isRecording;

		public void StartLocalPlayer() {
			if (_isRecording) return;

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

			_mic = mic;
			// Never send audio the DSP published before this consumer existed.
			mic.DiscardPendingFrames();

			// Diagnostic: the whole pipeline assumes 48 kHz. If the device records at
			// a different rate (e.g. 44100), each frame is slightly off → pitch shift +
			// periodic buffer underrun (audible "grésillement").
			if (clip.frequency != OpusConfig.SamplesPerSecond)
				Debug.LogWarning($"[VoiceMicInput] Microphone '{mic.Name}' records at {clip.frequency} Hz (expected {OpusConfig.SamplesPerSecond}). Resampling required.");
			return true;
		}

		private void Update() {
			if (!_isRecording || _mic == null) return;

			// Consume the DSP's own frames: re-reading the recording clip with another alignment
			// mixes processed and still-raw samples inside every frame (50 Hz crackle).
			while (_mic.TryDequeueProcessedFrame(out var frame)) {
				if (frame == null || frame.Length == 0)
					continue;

				int frameSize = OpusConfig.SamplesPerFrame;
				if (frameSize <= 0)
					continue;

				// Device rates other than 48 kHz: the encoder frame size is a hard requirement.
				if (frame.Length != frameSize)
					frame = Resample(frame, frameSize);

				// Idempotent mute boundary (the DSP runs on another update).
				if (_mic.IsMuted)
					Array.Clear(frame, 0, frame.Length);

				OnFrameReady?.Invoke(_frameIndex++, frame);
			}
		}

		/// <summary>Linear resample to the Opus frame size (devices not recording at 48 kHz).</summary>
		private static float[] Resample(float[] input, int length) {
			var output = new float[length];
			float step = (float)input.Length / length;

			for (int i = 0; i < length; i++) {
				float pos   = i * step;
				int   index = (int)pos;
				float frac  = pos - index;
				float a     = input[index];
				float b     = index + 1 < input.Length ? input[index + 1] : a;
				output[i]   = a + (b - a) * frac;
			}

			return output;
		}

		private void OnDestroy() {
			if (_isRecording) {
				_mic?.Stop("voice");
				_isRecording = false;
			}
		}
	}
}
