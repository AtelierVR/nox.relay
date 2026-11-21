using Nox.CCK.Utils;
using Nox.Relay.Core.Types.Content.Rooms;

namespace Nox.Relay.Core.Types.Entities {
	/// <summary>
	/// Broadcast (or reply) carrying the structure of a networked entity after a create / remove, the
	/// full entity list of the room on <see cref="EntityType.Sync"/>, or an ownership conversation
	/// step (<see cref="EntityType.Ownership"/> + <see cref="Sub"/>).
	/// <para>
	/// Transform comes from the transform channel and payload from the properties channel.
	/// </para>
	/// </summary>
	public class EntityEvent : RoomResponse {
		/// <summary>Operation that produced the event.</summary>
		public EntityType Op;

		/// <summary>Result of the operation.</summary>
		public EntityResult Result;

		/// <summary>Sub-operation of <see cref="EntityType.Ownership"/>.</summary>
		public EntityOwnership Sub;

		/// <summary>Id of the entity.</summary>
		public uint Id;

		/// <summary>Player that triggered the event (the requester on a forwarded ownership request).</summary>
		public uint SenderId;

		/// <summary>Player owning the entity.</summary>
		public uint OwnerId;

		/// <summary>Type of the entity: the CRC32 of its type name (<c>0</c> for a player).</summary>
		public uint Type;

		public byte Flags;

		/// <summary>Id of the ownership request (on <see cref="EntityOwnership.Requested"/> / a refusal).</summary>
		public ushort Request;

		public override bool FromBuffer(Buffer buffer) {
			buffer.Start();
			Op       = (EntityType)buffer.ReadByte();
			Result   = (EntityResult)buffer.ReadByte();
			Sub      = (EntityOwnership)buffer.ReadByte();
			Id       = buffer.ReadUInt();
			SenderId = buffer.ReadUInt();
			OwnerId  = buffer.ReadUInt();
			Type     = buffer.ReadUInt();
			Flags    = buffer.ReadByte();
			Request  = buffer.ReadUShort();
			return true;
		}

		public override string ToString()
			=> $"{GetType().Name}[room={Room?.InternalId}, op={Op}, sub={Sub}, result={Result}, id={Id}, type={Type}, owner={OwnerId}, request={Request}]";
	}
}
