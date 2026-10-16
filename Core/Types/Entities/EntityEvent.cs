using Nox.Relay.Core.Types.Content.Rooms;
using Buffer = Nox.CCK.Utils.Buffer;

namespace Nox.Relay.Core.Types.Entities {
	/// <summary>
	/// Broadcast (or reply) carrying the structure of a networked entity after a create / update /
	/// remove, or the full entity list of the room on <see cref="EntityType.Sync"/>.
	/// <para>
	/// Transform comes from the transform channel and payload from the properties channel.
	/// </para>
	/// </summary>
	public class EntityEvent : RoomResponse {
		public EntityType Type;
		public uint Id;
		public uint SenderId;
		public uint OwnerId;
		public string Kind;
		public byte Flags;

		public override bool FromBuffer(Buffer buffer) {
			buffer.Start();
			Type       = (EntityType)buffer.ReadByte();
			Id       = buffer.ReadUInt();
			SenderId = buffer.ReadUInt();
			OwnerId  = buffer.ReadUInt();
			Kind     = buffer.ReadString();
			Flags    = buffer.ReadByte();
			return true;
		}

		public override string ToString()
			=> $"{GetType().Name}[room={Room?.InternalId}, op={Type}, id={Id}, kind='{Kind}', owner={OwnerId}, flags={Flags}]";
	}
}
