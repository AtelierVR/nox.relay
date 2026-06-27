using System;
using Nox.Audio.Players;
using UnityEngine;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>
	/// Abstract voice input — MetaVoiceChat VcAudioInput equivalent.
	/// Derive to implement microphone or other audio sources.
	/// </summary>
	public abstract class NoxVoiceInput : MonoBehaviour {
		public NoxVoiceChat VoiceChat;
		[Tooltip("Optional first input filter in the pipeline.")]
		public NoxVoiceInputFilter OptionalFirstInputFilter;

		/// <summary>Fired when a new audio frame is ready. Receives (frameIndex, pcmSamples).</summary>
		public event Action<int, float[]> OnFrameReady;

		/// <summary>Start capturing for the local player.</summary>
		public abstract void StartLocalPlayer();

		/// <summary>Send frame through filter chain then fire OnFrameReady.</summary>
		protected void SendAndFilterFrame(int index, float[] samples) {
			if (OptionalFirstInputFilter != null)
				OptionalFirstInputFilter.FilterRecursively(index, ref samples);
			OnFrameReady?.Invoke(index, samples);
		}
	}

	/// <summary>
	/// Abstract input filter — apply processing (noise suppression, etc.) before encoding.
	/// </summary>
	public abstract class NoxVoiceInputFilter : MonoBehaviour {
		[Tooltip("Next filter in the chain.")]
		public NoxVoiceInputFilter NextFilter;

		protected abstract void Filter(int index, ref float[] samples);

		public void FilterRecursively(int index, ref float[] samples) {
			Filter(index, ref samples);
			if (NextFilter != null)
				NextFilter.FilterRecursively(index, ref samples);
		}
	}
}
