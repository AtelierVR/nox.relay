namespace Nox.Relay.Core.Types.Handshakes {
	/// <summary>
	/// Flags used during the handshake process.
	/// Is indicate the information about the server.
	/// </summary>
	public enum HandshakeFlags : byte {
		None      = 0,
		IsOffline = 1 << 0,
	}
}