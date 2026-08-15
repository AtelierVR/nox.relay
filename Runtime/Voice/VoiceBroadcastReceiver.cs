using System;
using Nox.CCK.Audio.Opus;
using UnityEngine;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>
	/// Global 2D broadcast voice receiver — created on-the-fly by Session when a
	/// Broadcast-mode StreamEvent arrives from a speaker whose physical is not
	/// instantiated locally. Each instance decodes and plays globally (spatialBlend=0).
	/// </summary>
	[RequireComponent(typeof(VoiceAudioSourceOutput))]
	public class VoiceBroadcastReceiver : MonoBehaviour {
		public VoiceAudioSourceOutput Output { get; private set; }

		public int SpeakerId { get; private set; }
		public bool IsStarted { get; private set; }

		private OpusDecoder.OpusDecoderInstance _decoder;
		private VoiceJitter _jitter;

		private void Awake() {
			Output = GetComponent<VoiceAudioSourceOutput>();
		}

		/// <summary>Initialize for a broadcast speaker. Sets the AudioSource to 2D (spatialBlend=0).</summary>
		public void Initialize(int speakerId) {
			if (IsStarted) return;

			SpeakerId = speakerId;
			gameObject.name = $"BroadcastReceiver_{speakerId}";

			_decoder = new OpusDecoder.OpusDecoderInstance(OpusConfig.SamplesPerSecond, 1);
			_jitter = new VoiceJitter();

			// Configure AudioSource for 2D global broadcast
			var source = Output.AudioSource;
			if (source == null) {
				source = gameObject.AddComponent<AudioSource>();
				Output.AudioSource = source;
			}
			source.spatialBlend = 0f;    // 2D — no distance attenuation
			source.dopplerLevel = 0f;
			source.spatialize = false;

			IsStarted = true;
		}

		/// <summary>Feed an encoded voice frame (decodes and plays 2D).</summary>
		public void ReceiveFrame(int index, double timestamp, float additionalLatency, ReadOnlySpan<byte> data) {
			if (!IsStarted) return;

			float targetLatency = (OpusConfig.SecondsPerFrame * VoiceConfig.OutputMinBufferFrames)
				+ Time.deltaTime + additionalLatency
				+ _jitter.Update(timestamp);

			if (data.Length == 0) {
				Output.ReceiveFrame(index, null, targetLatency);
				return;
			}

			float[] samples = null;
			try {
				samples = _decoder.Decode(data.ToArray(), OpusConfig.SamplesPerFrame);
			} catch (Exception ex) {
				Debug.LogWarning($"[VoiceBroadcastReceiver] Opus decode failed: {ex.Message}");
			}

			if (samples != null && samples.Length == OpusConfig.SamplesPerFrame)
				Output.ReceiveFrame(index, samples, targetLatency);
			else
				Output.ReceiveFrame(index, null, targetLatency);
		}

		private void OnDestroy() {
			_decoder?.Dispose();
			_decoder = null;
			_jitter?.Reset();
			_jitter = null;
		}
	}
}
