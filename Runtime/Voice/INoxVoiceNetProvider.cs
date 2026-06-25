using System;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>
	/// Network provider interface — MetaVoiceChat INetProvider equivalent.
	/// Implement this to bridge voice chat with your networking layer.
	/// </summary>
	public interface INoxVoiceNetProvider {
		/// <summary>Is the local player deafened (doesn't want to hear others)?</summary>
		bool IsLocalPlayerDeafened { get; }

		/// <summary>
		/// Relay an encoded voice frame to all other players.
		/// </summary>
		/// <param name="index">Monotonic frame index.</param>
		/// <param name="timestamp">Sender timestamp.</param>
		/// <param name="data">Opus-encoded audio data.</param>
		void RelayFrame(int index, double timestamp, ReadOnlySpan<byte> data);
	}
}
