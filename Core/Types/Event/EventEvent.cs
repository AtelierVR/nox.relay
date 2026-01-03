using Nox.Relay.Core.Types.Content.Rooms;
using Buffer = Nox.CCK.Utils.Buffer;

namespace Nox.Relay.Core.Types.Event {
	/// <summary>
	/// Represents an event sent within a room.
	/// Events are used for custom communication between players in the room.
	/// </summary>
	public class EventEvent : RoomResponse {
		/// <summary>
		/// Id of <see cref="Nox.Relay.Core.Players.Player"/> who sent the event.
		/// </summary>
		public ushort SenderId;
		
		/// <summary>
		/// Name of the event.
		/// Is commonly a <see cref="Nox.CCK.Network.Serializer.Hash"/> of a string.
		/// </summary>
		public long   Name;
		
		/// <summary>
		/// Payload of the event.
		/// </summary>
		public byte[] Payload;

		public override bool FromBuffer(Buffer buffer) {
			buffer.Start();

			SenderId = buffer.ReadUShort();
			Name     = buffer.ReadLong();
			var length = buffer.ReadUShort();
			Payload = buffer.ReadBytes(length);

			return true;
		}

		public override string ToString()
			=> $"{GetType().Name}[InternalId={Room.InternalId}, SenderId={SenderId}, Name={Name}, PayloadLength={Payload.Length}]";
	}
}