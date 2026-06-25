using UnityEngine;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>
	/// Global 2D broadcast voice receiver — created on-the-fly by Session
	/// when a Broadcast-mode StreamEvent arrives from a speaker whose
	/// physical is not instantiated locally.
	/// <para>
	/// Each instance wraps a NoxVoiceChat with a NoxVoiceAudioSourceOutput
	/// configured for spatialBlend=0 (2D, no distance attenuation).
	/// </para>
	/// </summary>
	[RequireComponent(typeof(NoxVoiceChat))]
	[RequireComponent(typeof(NoxVoiceAudioSourceOutput))]
	public class NoxVoiceBroadcastReceiver : MonoBehaviour {
		public NoxVoiceChat VoiceChat { get; private set; }
		public NoxVoiceAudioSourceOutput Output { get; private set; }

		public int SpeakerId { get; private set; }
		public bool IsStarted { get; private set; }

		private void Awake() {
			VoiceChat = GetComponent<NoxVoiceChat>();
			Output = GetComponent<NoxVoiceAudioSourceOutput>();
		}

		/// <summary>
		/// Initialize for a broadcast speaker. Sets the AudioSource to 2D
		/// (spatialBlend=0) so everyone hears the speaker globally.
		/// </summary>
		public void Initialize(int speakerId, NoxVoiceConfig config, Core.Rooms.Room room) {
			if (IsStarted) return;

			SpeakerId = speakerId;
			gameObject.name = $"BroadcastReceiver_{speakerId}";

			VoiceChat.Config = config;
			VoiceChat.AudioOutput = Output;
			Output.VoiceChat = VoiceChat;

			// Configure AudioSource for 2D global broadcast
			var source = Output.AudioSource;
			if (source == null) {
				source = gameObject.AddComponent<AudioSource>();
				Output.AudioSource = source;
			}
			source.spatialBlend = 0f;    // 2D — no distance attenuation
			source.dopplerLevel = 0f;
			source.spatialize = false;

			// Start the voice client (receive-only — isLocalPlayer=false)
			var netProvider = GetComponent<NoxVoiceRelayProvider>();
			if (netProvider == null)
				netProvider = gameObject.AddComponent<NoxVoiceRelayProvider>();
			VoiceChat.StartClient(netProvider, isLocalPlayer: false, maxDataBytesPerPacket: 1000);
			IsStarted = true;
		}

		/// <summary>Feed a decoded voice frame directly (bypasses relay provider).</summary>
		public void ReceiveFrame(int index, double timestamp, float additionalLatency, System.ReadOnlySpan<byte> data) {
			if (!IsStarted) return;
			VoiceChat.ReceiveFrame(index, timestamp, additionalLatency, data);
		}

		private void OnDestroy() {
			VoiceChat?.StopClient();
		}
	}
}
