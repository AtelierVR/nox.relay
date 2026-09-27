using Nox.Audio.Players;
using UnityEngine;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>
	/// Exposes a remote player's playback (<see cref="VoiceAudioSourceOutput"/>) as an
	/// <see cref="ICapturedAudio"/>, so consumers (nameplate, UI) read its <see cref="Level"/> the
	/// same way they read the local microphone's.
	/// </summary>
	public sealed class RemoteCapturedAudio : ICapturedAudio {
		private readonly VoiceAudioSourceOutput _output;

		public RemoteCapturedAudio(VoiceAudioSourceOutput output)
			=> _output = output;

		/// <inheritdoc/>
		public AudioClip Clip
			=> _output != null && _output.AudioSource != null ? _output.AudioSource.clip : null;

		/// <inheritdoc/>
		public int Position
			=> _output != null && _output.AudioSource != null ? _output.AudioSource.timeSamples : 0;

		/// <inheritdoc/>
		public float Level
			=> _output != null ? _output.Level : 0f;
	}
}
