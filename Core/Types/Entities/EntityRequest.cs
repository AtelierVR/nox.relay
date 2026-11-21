using Nox.CCK.Utils;
using Nox.Relay.Core.Types.Contents.Rooms;

namespace Nox.Relay.Core.Types.Entities {
	/// <summary>
	/// Request against the entities of the room: create one, remove it, ask for the full list, or ask
	/// for the ownership of one.
	/// <para>
	/// A create carries no id: the relay assigns it. It only names the <see cref="Type"/> the client
	/// needs (the CRC32 of a type name, the CRC32 of the world object for an entity placed in the
	/// world).
	/// </para>
	/// <para>
	/// This packet only carries the entity structure. The transform is sent through the transform
	/// channel (initial position included) and the payload through the properties channel.
	/// </para>
	/// </summary>
	public class EntityRequest : RoomRequest {
		/// <summary>Operation to perform.</summary>
		public EntityType Op = EntityType.Sync;

		/// <summary>Sub-operation of <see cref="EntityType.Ownership"/>.</summary>
		public EntityOwnership Sub;

		/// <summary>Target entity id (remove) or entity whose ownership is wanted.</summary>
		public uint Id;

		/// <summary>Type of the entity (CRC32), only used by a create.</summary>
		public uint Type;

		/// <summary>Entity behaviour flags (see <see cref="EntityFlags"/>).</summary>
		public byte Flags;

		/// <summary>Player the ownership is handed to (<c>0</c> = the sender itself).</summary>
		public uint PlayerId;

		/// <summary>Ownership request answered by a <see cref="EntityOwnership.Refuse"/>.</summary>
		public ushort Request;

		public override Buffer ToBuffer() {
			var buffer = new Buffer();
			buffer.Write((byte)Op);

			switch (Op) {
				case EntityType.Create:
					buffer.Write(Type);
					buffer.Write(Flags);
					break;

				case EntityType.Remove:
					buffer.Write(Id);
					break;

				case EntityType.Ownership:
					buffer.Write((byte)Sub);

					if (Sub == EntityOwnership.Change) {
						buffer.Write(Id);
						buffer.Write(PlayerId);
					} else {
						buffer.Write(Request);
					}

					break;
			}

			return buffer;
		}

		public override string ToString()
			=> $"{GetType().Name}[room={Room?.InternalId}, op={Op}, sub={Sub}, id={Id}, type={Type}, player={PlayerId}, request={Request}]";
	}
}
