using UnityEngine;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>
	/// Abstract voice output — MetaVoiceChat VcAudioOutput equivalent.
	/// Derive to implement AudioSource-based or other output.
	/// </summary>
	public abstract class NoxVoiceOutput : MonoBehaviour {
		public NoxVoiceChat VoiceChat;
		[Tooltip("Optional first output filter in the pipeline.")]
		public NoxVoiceOutputFilter OptionalFirstOutputFilter;

		/// <summary>Receive a decoded audio frame.</summary>
		protected abstract void ReceiveFrame(int index, float[] samples, float targetLatency);

		/// <summary>Receive frame through filter chain then deliver.</summary>
		public void ReceiveAndFilterFrame(int index, float[] samples, float targetLatency) {
			if (OptionalFirstOutputFilter != null)
				OptionalFirstOutputFilter.FilterRecursively(index, samples, targetLatency);
			ReceiveFrame(index, samples, targetLatency);
		}
	}

	/// <summary>
	/// Abstract output filter — apply post-processing before playback.
	/// </summary>
	public abstract class NoxVoiceOutputFilter : MonoBehaviour {
		[Tooltip("Next filter in the chain.")]
		public NoxVoiceOutputFilter NextFilter;

		protected abstract void Filter(int index, float[] samples, float targetLatency);

		public void FilterRecursively(int index, float[] samples, float targetLatency) {
			Filter(index, samples, targetLatency);
			if (NextFilter != null)
				NextFilter.FilterRecursively(index, samples, targetLatency);
		}
	}
}
