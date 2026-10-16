using Nox.Relay.Core.Types.Contents.Rooms;
using Buffer = Nox.CCK.Utils.Buffer;

namespace Nox.Relay.Core.Types.Entities {
	/// <summary>
	/// Request against an entity of the room: create it, update it, remove it, or ask for
	/// the full list. The <see cref="Kind"/> tells the relay (and the other clients) which entity
	/// type is concerned (a player, <c>"prop"</c>, <c>"mirror"</c>, ...).
	/// <para>
	/// This packet only carries the entity structure. The transform is sent through the transform
	/// channel (initial position included) and the payload through the properties channel.
	/// </para>
	/// </summary>
	public class EntityRequest : RoomRequest {
		/// <summary>Operation to perform.</summary>
		public EntityType Op = EntityType.Sync;

		/// <summary>Entity id (<c>0</c> on <see cref="EntityType.Create"/>: the relay assigns it).</summary>
		public uint Id;

		/// <summary>Entity kind (free-form string identifying the entity type).</summary>
		public string Kind;

		/// <summary>Entity behaviour flags (meaning depends on the kind).</summary>
		public byte Flags;

		public override Buffer ToBuffer() {
			var buffer = new Buffer();
			buffer.Write((byte)Op);
			buffer.Write(Id);
			buffer.Write(Kind ?? string.Empty);
			buffer.Write(Flags);
			return buffer;
		}

		public override string ToString()
			=> $"{GetType().Name}[room={Room?.InternalId}, op={Op}, id={Id}, kind='{Kind}', flags={Flags}]";
	}
}
