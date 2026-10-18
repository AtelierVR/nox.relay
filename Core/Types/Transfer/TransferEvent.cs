using Buffer = Nox.CCK.Utils.Buffer;
using Nox.Relay.Core.Types.Contents;
using Nox.CCK.Utils;

namespace Nox.Relay.Core.Types.Transfer {
	/// <summary>
	/// Sent by the relay to ask the client to go somewhere. The <see cref="Type"/> byte tells which
	/// layout follows:
	/// <code>
	/// [len][state][0x17][type = Server] [address: string][token: prefixed bytes][reason: string]
	/// [len][state][0x17][type = Room]   [room: u8][reason: string]
	/// </code>
	/// <para>
	/// <b>Server</b>: another relay. The <see cref="Token"/> must be presented in the next handshake
	/// (see <see cref="Handshakes.HandshakeRequest.TransferToken"/>) so the target relay can
	/// authenticate the client without a challenge-response round-trip; the target picks the room
	/// (see its handshake <c>auto_room</c>).
	/// </para>
	/// <para>
	/// <b>Room</b>: another room of the current relay. The transport is kept, only the room
	/// membership is rebuilt.
	/// </para>
	/// </summary>
	public class TransferEvent : ContentResponse {
		/// <summary>
		/// Which layout the payload uses.
		/// </summary>
		public TransferType Type;

		/// <summary>
		/// Host of the target relay, formatted as <c>host:port</c> (<see cref="TransferType.Server"/> only).
		/// </summary>
		public string Address;

		/// <summary>
		/// Opaque, client-unique token (arbitrary bytes) to present in the next handshake
		/// (<see cref="TransferType.Server"/> only). Empty when no credential is involved.
		/// </summary>
		public byte[] Token;

		/// <summary>
		/// Room to join (<see cref="TransferType.Room"/> only), as listed by the relay
		/// (<see cref="Nox.Relay.Core.Rooms.Room.InternalId"/>).
		/// </summary>
		public byte Room;

		/// <summary>
		/// Human-readable reason (may be empty).
		/// </summary>
		public string Reason;

		/// <summary>Whether the client has to change relay (and therefore reconnect).</summary>
		public bool IsRelayChange
			=> Type == TransferType.Server;

		/// <summary>Whether this is a room move on the current relay.</summary>
		public bool IsRoomMove
			=> Type == TransferType.Room;

		public override bool FromBuffer(Buffer buffer) {
			buffer.Start();

			Type = buffer.ReadEnum<TransferType>();

			switch (Type) {
				case TransferType.Server:
					Address = buffer.ReadString();
					Token   = buffer.ReadBytes(buffer.ReadUShort());
					Reason  = buffer.ReadString();

					if (string.IsNullOrEmpty(Address)) {
						Logger.LogWarning("Invalid transfer packet: the relay address is empty");
						return false;
					}

					break;

				case TransferType.Room:
					Room   = buffer.ReadByte();
					Reason = buffer.ReadString();
					break;

				default:
					Logger.LogWarning($"Invalid transfer packet: unknown type {(byte)Type}");
					return false;
			}

			return true;
		}

		public override string ToString()
			=> IsRelayChange
				? $"{GetType().Name}[Server, Address={Address}, Token=({Token?.Length ?? 0} bytes), Reason={Reason}]"
				: $"{GetType().Name}[Room={Room}, Reason={Reason}]";
	}
}
