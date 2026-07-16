using Nox.Audio.Runtime.Microphone;
using UnityEngine;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>
	/// Bridges <see cref="MicrophoneProcessor"/> (nox.audio) into the
	/// <see cref="NoxVoiceInputFilter"/> chain. Reads settings from the current
	/// microphone via <see cref="MicrophoneManager"/>.
	/// </summary>
	public class MicrophoneInputFilter : NoxVoiceInputFilter {
		[Tooltip("Direct reference. If null, resolved via Main.MicrophoneManager.")]
		public MicrophoneManager Manager;

		private MicrophoneProcessor _processor;

		private MicrophoneManager ResolveManager() {
			if (Manager != null) return Manager;
			return Nox.Audio.Runtime.Main.MicrophoneManager;
		}

		private void Awake() {
			_processor = new MicrophoneProcessor();
		}

		protected override void Filter(int index, ref float[] samples) {
			var mic = ResolveManager()?.Current;
			_processor.Process(samples, mic as Nox.Audio.Runtime.Microphone.Microphone);
		}
	}
}
