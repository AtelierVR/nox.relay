using System.Collections.Generic;
using Nox.Microphone.Players;
using UnityEngine;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>
	/// PCM playback queue + AudioClip + AudioSource.
	/// Pattern identique à OpusRoundTripTest :
	///   - Feed() depuis le main thread → queue thread-safe
	///   - OnPCMRead() depuis le thread audio → draine la queue
	///   - AudioClip stream=true, AudioSource loop=true
	/// </summary>
	public class VoicePlayback {
		private AudioSource      _source;
		private readonly VoiceConfig _config;

		public AudioSource Source => _source;

		private readonly Queue<float> _pcmQueue  = new();
		private readonly object       _queueLock = new();

		private int _sampleRate;
		private int _channels = 1;

		/// <summary>Buffer de pré-charge avant Play() — ~120 ms.</summary>
		private int MinBuffer => (int)(_sampleRate * 0.12f);

		// ── Construction ──

		public VoicePlayback(GameObject target, VoiceConfig config, bool muted = false) {
			_config     = config;
			_sampleRate = config?.SampleRate ?? 48000;

			_source = target.GetComponent<AudioSource>();
			if (!_source)
				_source = target.AddComponent<AudioSource>();

			if (muted) {
				_source.mute         = true;
				_source.spatialize   = false;
				_source.spatialBlend = 0f;
			} else {
				_source.spatialize        = true;
				_source.spatialBlend      = 1f;
				_source.rolloffMode       = AudioRolloffMode.Linear;
				_source.minDistance       = 1f;
				_source.maxDistance       = 20f;
			}

			_source.loop        = true;
			_source.playOnAwake = false;
			_source.volume      = 1f;

			AttachStreamClip();
		}

		// ── Migration vers AudioSource du VoiceAvatarModule ──

		public void SetSource(AudioSource newSource) {
			if (newSource == null || newSource == _source) return;
			if (_source) { _source.Stop(); _source.clip = null; }

			_source = newSource;
			_source.spatialize        = true;
			_source.spatialBlend      = 1f;
			_source.rolloffMode       = AudioRolloffMode.Linear;
			_source.minDistance       = 1f;
			_source.maxDistance       = 20f;
			_source.loop              = true;
			_source.playOnAwake       = false;
			_source.volume            = 1f;
			_source.mute              = false;

			AttachStreamClip();

			int n; lock (_queueLock) n = _pcmQueue.Count;
			if (n >= MinBuffer) _source.Play();
		}

		// ── Volume / spatial ──

		public void ApplyMode(SpeakMode mode) {
			if (_config == null || !_source) return;
			_source.volume       = _config.GetVolume(mode);
			_source.spatialBlend = _config.GetSpatialBlend(mode);
			_source.rolloffMode  = _config.GetRolloff(mode);
			_source.maxDistance  = mode == SpeakMode.Broadcast
				? float.MaxValue : _config.GetRange(mode);
		}

		// ── Feed (main thread, comme le Receive() du test) ──

		public void Feed(float[] samples) {
			if (samples == null || samples.Length == 0) return;
			lock (_queueLock) {
				foreach (float s in samples) _pcmQueue.Enqueue(s);
			}

			if (_source && !_source.isPlaying) {
				int n; lock (_queueLock) n = _pcmQueue.Count;
				if (n >= MinBuffer) _source.Play();
			}
		}

		public void Flush() { }

		// ── AudioClip + PCMReaderCallback (comme le test) ──

		private void AttachStreamClip() {
			_source.clip = AudioClip.Create("voice_stream",
				_sampleRate * 30, _channels, _sampleRate, true, OnPCMRead);
		}

		private void OnPCMRead(float[] data) {
			int filled = 0;
			lock (_queueLock) {
				while (filled < data.Length && _pcmQueue.Count > 0)
					data[filled++] = _pcmQueue.Dequeue();
			}
			for (int i = filled; i < data.Length; i++) data[i] = 0f;
		}

		public void Dispose() {
			if (_source) _source.Stop();
			lock (_queueLock) _pcmQueue.Clear();
		}
	}
}
