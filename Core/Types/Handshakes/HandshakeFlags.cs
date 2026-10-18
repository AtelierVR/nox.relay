namespace Nox.Relay.Core.Types.Handshakes {
	/// <summary>
	/// Flags used during the handshake process.
	/// Is indicate the information about the server.
	/// </summary>
	public enum HandshakeFlags : byte {
		None      = 0,
		IsOffline = 1 << 0,

		/// <summary>
		/// The relay resolved the transfer token: the client is already authenticated and can skip
		/// the challenge-response exchange.
		/// </summary>
		Transferred = 1 << 1,

		/// <summary>
		/// The response carries the room a client should join when it has no valid target of its own
		/// (standalone relay, which has a single room, or a public hub).
		/// </summary>
		AutoRoom = 1 << 2,
	}
}