using System;
using Nox.CCK.Players;
using Nox.CCK.Utils;
using Nox.Entities;
using Nox.Relay.Core.Types.Contents.Rooms;
using Buffer = Nox.CCK.Utils.Buffer;

namespace Nox.Relay.Core.Types.Transform {
	/// <summary>
	/// Request to transform an object in a room.
	/// </summary>
	public class TransformRequest : RoomRequest {
		/// <summary>
		/// Type of transform target.
		/// </summary>
		public TransformType Type;

		/// <summary>
		/// Transform data.
		/// </summary>
		public TransformObject Transform;

		/// <summary>
		/// Id of the entity to apply the transform to.
		/// <remarks>Is filled when <see cref="Type"/> is <see cref="TransformType.EntityPart"/>.</remarks>
		/// If the Id is <see cref="ushort.MaxValue"/>, the transform applies to self player.
		/// </summary>
		public ushort EntityId;

		/// <summary>
		/// Rig index of the part to apply the transform to.
		/// <remarks>Is filled when <see cref="Type"/> is <see cref="TransformType.EntityPart"/>.</remarks>
		/// </summary>
		public ushort PartRig;

		/// <summary>
		/// Path to the object to apply the transform to.
		/// <remarks>Is filled when <see cref="Type"/> is <see cref="TransformType.ByPath"/>.</remarks>
		/// A path is a string representing the hierarchy of objects in the scene,
		/// separated by slashes (/). For example: "0/Root/.../Child/Object".
		/// And the first segment (before the first slash) is usually the index of the dimension.
		/// </summary>
		public string Path;

		/// <summary>
		/// Creates a <see cref="TransformRequest"/> for a specific part of a player.
		/// </summary>
		/// <param name="playerId"></param>
		/// <param name="part"></param>
		/// <returns></returns>
		public static TransformRequest CreatePart(ushort playerId, IPart part)
			=> new() {
				Type      = TransformType.EntityPart,
				EntityId  = playerId,
				PartRig   = part.Id,
				Transform = ToTransform(part)
			};

		/// <summary>
		/// Converts an <see cref="IPart"/> to a <see cref="TransformObject"/>.
		/// </summary>
		/// <param name="part"></param>
		/// <returns></returns>
		private static TransformObject ToTransform(IPart part) {
			var transform = new TransformObject();
			transform.SetPosition(part.Position);
			transform.SetRotation(part.Rotation);
			transform.SetScale(part.Scale);
			transform.SetVelocity(part.Velocity);
			transform.SetAngular(part.Angular);
			return transform;
		}

		/// <summary>
		/// Creates a <see cref="TransformRequest"/> for an object by its path.
		/// </summary>
		/// <param name="path"></param>
		/// <param name="transform"></param>
		/// <returns></returns>
		public static TransformRequest CreateByPath(string path, TransformObject transform)
			=> new() {
				Type      = TransformType.ByPath,
				Path      = path,
				Transform = transform
			};

		/// <summary>
		/// Creates a <see cref="TransformRequest"/> for a specific part of an entity.
		/// </summary>
		/// <param name="entityId"></param>
		/// <param name="rig"></param>
		/// <param name="transform"></param>
		/// <returns></returns>
		public static TransformRequest CreateEntity(ushort entityId, ushort rig, TransformObject transform)
			=> new() {
				Type      = TransformType.EntityPart,
				PartRig   = rig,
				EntityId  = entityId,
				Transform = transform
			};

		/// <summary>
		/// Creates a <see cref="TransformRequest"/> for a specific part of the player entity.
		/// </summary>
		/// <param name="entityId"></param>
		/// <param name="transform"></param>
		/// <returns></returns>
		public static TransformRequest CreateEntity(ushort entityId, TransformObject transform)
			=> CreateEntity(entityId, PlayerRig.Base.ToIndex(), transform);

		public override Buffer ToBuffer() {
			var buffer = new Buffer();
			buffer.Write(Type);

			switch (Type) {
				case TransformType.EntityPart:
					buffer.Write(EntityId);
					buffer.Write(PartRig);
					break;
				case TransformType.ByPath:
					buffer.Write(Path);
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(Type), $"Unsupported TransformType: {Type}");
			}

			buffer.Write(Transform.Flags);
			if (Transform.Flags.HasFlag(TransformFlags.Position))
				buffer.Write(Transform.GetPosition());
			if (Transform.Flags.HasFlag(TransformFlags.Rotation))
				buffer.Write(Transform.GetRotation());
			if (Transform.Flags.HasFlag(TransformFlags.Scale))
				buffer.Write(Transform.GetScale());
			if (Transform.Flags.HasFlag(TransformFlags.Velocity))
				buffer.Write(Transform.GetVelocity());
			if (Transform.Flags.HasFlag(TransformFlags.Angular))
				buffer.Write(Transform.GetAngular());

			return buffer;
		}

		public override string ToString()
			=> $"{GetType().Name}[InternalId={Room.InternalId}, Type={Type}"
				+ $"{(Type == TransformType.EntityPart ? $", EntityId={EntityId}, PartRig={PartRig}" : "")}"
				+ $"{(Type == TransformType.ByPath ? $", Path={Path}" : "")}"
				+ $", Transform={Transform}]";
	}
}