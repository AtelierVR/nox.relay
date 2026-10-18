namespace Nox.Relay.Core.Types.Transfer {
	/// <summary>
	/// Operation carried by a <see cref="TransferEvent"/> (the <c>op</c> byte).
	/// Byte values match the Rust <c>TransferOp</c> enum from <c>relay/src/transfer.rs</c>.
	/// </summary>
	public enum TransferType : byte {
		/// <summary>
		/// Change relay: <c>[address][token][reason]</c>. The client reconnects and presents the
		/// token in its next handshake.
		/// </summary>
		Server = 0,

		/// <summary>
		/// Change room on the same relay: <c>[room][reason]</c>. The transport is kept.
		/// </summary>
		Room = 1,
	}
}
