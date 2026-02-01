using Nox.CCK.Utils;
using Nox.Relay.Core.Types.Content.Rooms;

namespace Nox.Relay.Core.Types.Transform {
	/// <summary>
	/// Represents a transform event within a room.
	/// Transform events are used to update the position, rotation, scale, velocity,
	/// and angular velocity of entities or objects in the room.
	/// </summary>
	public class TransformEvent : RoomResponse {
		/// <summary>
		/// The type of transform being represented.
		/// </summary>
		public TransformType Type;

		/// <summary>
		/// The transform data associated with this event.
		/// </summary>
		public TransformObject Transform;

		/// <summary>
		/// The ID of the entity (usually a player) whose part is being transformed.
		/// <remarks>Is filled when <see cref="Type"/> is <see cref="TransformType.EntityPart"/>.</remarks>
		/// </summary>
		public ushort EntityId;

		/// <summary>
		/// The rig index of the part being transformed.
		/// <remarks>Is filled when <see cref="Type"/> is <see cref="TransformType.EntityPart"/>.</remarks>
		/// </summary>
		public ushort PartRig;

		/// <summary>
		/// The path to the object being transformed.
		/// <remarks>Is filled when <see cref="Type"/> is <see cref="TransformType.ByPath"/>.</remarks>
		/// A path is a string representing the hierarchy of objects in the scene,
		/// separated by slashes (/). For example: "0/Root/.../Child/Object".
		/// And the first segment (before the first slash) is usually the index of the dimension.
		/// </summary>
		public string Path;

		/// <summary>
		/// Id of <see cref="Nox.Relay.Core.Players.Player"/> who sent the transform event.
		/// </summary>
		public ushort SenderId;

		public override bool FromBuffer(Buffer buffer) {
			buffer.Start();
			
			Type = buffer.ReadEnum<TransformType>();

			switch (Type) {
				case TransformType.EntityPart:
					EntityId = buffer.ReadUShort();
					PartRig  = buffer.ReadUShort();
					break;
				case TransformType.ByPath:
					Path = buffer.ReadString();
					break;
				default:
					return false;
			}

			Transform = new TransformObject();
			var flags = buffer.ReadEnum<TransformFlags>();
			if (flags.HasFlag(TransformFlags.Position))
				Transform.SetPosition(buffer.ReadVector3());
			if (flags.HasFlag(TransformFlags.Rotation))
				Transform.SetRotation(buffer.ReadQuaternion());
			if (flags.HasFlag(TransformFlags.Scale))
				Transform.SetScale(buffer.ReadVector3());
			if (flags.HasFlag(TransformFlags.Velocity))
				Transform.SetVelocity(buffer.ReadVector3());
			if (flags.HasFlag(TransformFlags.Angular))
				Transform.SetAngular(buffer.ReadVector3());

			SenderId = buffer.ReadUShort();
			return true;
		}

		public override string ToString()
			=> $"{GetType().Name}[InternalId={Room.InternalId}, Type={Type}"
				+ $"{(Type == TransformType.EntityPart ? $", EntityId={EntityId}, PartRig={PartRig}" : "")}"
				+ $"{(Type == TransformType.ByPath ? $", Path={Path}" : "")}"
				+ $", Transform={Transform}, ByEntityId={SenderId}]";
	}
}