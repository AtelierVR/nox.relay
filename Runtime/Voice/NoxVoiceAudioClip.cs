using System;
using UnityEngine;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>
	/// Circular AudioClip for voice playback — MetaVoiceChat VcAudioClip equivalent.
	/// Manages a looping AudioClip with frame-indexed write positions.
	/// </summary>
	public class NoxVoiceAudioClip : IDisposable {
		private readonly int _samplesPerFrame;
		private readonly int _framesPerClip;
		private readonly AudioClip _audioClip;

		private readonly float[] _emptyFrame;
		private readonly float[] _emptyClip;

		public float Length => _audioClip.length;

		public NoxVoiceAudioClip(NoxVoiceConfig config, AudioSource audioSource) {
			_samplesPerFrame = config.SamplesPerFrame;
			_framesPerClip = config.FramesPerClip;

			_audioClip = AudioClip.Create(nameof(NoxVoiceAudioClip),
				NoxVoiceConfig.SamplesPerClip, channels: 1,
				NoxVoiceConfig.SamplesPerSecond, stream: false);

			_emptyFrame = new float[_samplesPerFrame];
			_emptyClip = new float[NoxVoiceConfig.SamplesPerClip];

			audioSource.playOnAwake = false;
			audioSource.Stop();
			audioSource.loop = true;
			audioSource.clip = _audioClip;
		}

		/// <summary>Write a frame of PCM samples at the given frame offset.</summary>
		public void WriteFrame(int offsetFrames, float[] samples) {
			samples ??= _emptyFrame;

			if (samples.Length != _samplesPerFrame) {
				Debug.LogWarning("[NoxVoiceAudioClip] Sample count mismatch with config!");
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
