using Nox.CCK.Utils;
using Nox.Relay.Core.Rooms;
using Nox.Relay.Core.Types.Rooms;

namespace Nox.Relay.Core.Types.Contents.Rooms {
	/// <summary>
	/// Response containing a list of rooms with pagination details.
	/// </summary>
	public class RoomsResponse : ContentResponse {
		/// <summary>
		/// The list of room instances returned in the response.
		/// </summary>
		public Room[] Instances;

		/// <summary>
		/// The current page number of the room list.
		/// </summary>
		public byte Page;

		/// <summary>
		/// The total number of pages available for the room list.
		/// </summary>
		public byte PageCount;

		public override bool FromBuffer(Buffer buffer) {
			buffer.Start();
			var instanceCount = buffer.ReadByte();
			var instances     = new Room[instanceCount];
			for (var i = 0; i < instanceCount; i++)
				instances[i] = new Room {
					Connection     = Connection,
					Flags          = buffer.ReadEnum<RoomFlags>(),
					InternalId     = buffer.ReadByte(),
					NodeId         = buffer.ReadUInt(),
					PlayerCount    = buffer.ReadUShort(),
					MaxPlayerCount = buffer.ReadUShort(),
				};
			Page      = buffer.ReadByte();
			PageCount = buffer.ReadByte();
			Instances = instances;
			return true;
		}

		public override string ToString()
			=> $"{GetType().Name}[Instances={Instances.Length}, Page={Page}/{PageCount}]";
	}
}