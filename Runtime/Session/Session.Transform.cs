using System.Collections.Generic;
using Nox.CCK.Utils;
using Nox.Entities;
using Nox.Relay.Core.Types.Transform;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Relay.Runtime {
	/// <summary>Transform synchronization of every entity part (players included) and of object paths.</summary>
	public sealed partial class Session {
		internal void OnTransformHandler(TransformEvent @event) {
			switch (@event.Type) {
				case TransformType.EntityPart:
					ApplyEntityPartTransform(@event);
					break;

				case TransformType.ByPath:
					// TODO: Implement path-based transform when needed.
					Logger.LogWarning($"Transform by path not yet implemented: Path={@event.Path}", tag: Tag);
					break;

				default:
					Logger.LogWarning($"Unknown transform type: {@event.Type}", tag: Tag);
					break;
			}
		}

		/// <summary>
		/// Apply a part transform to its entity. A player is an entity: the id is resolved once and the
		/// part is taken straight from the entity, whatever its kind (a player rig part, a prop
		/// sub-element, ...).
		/// </summary>
		private void ApplyEntityPartTransform(TransformEvent @event) {
			var entity = _entities.GetEntity<Entity>(@event.EntityId);
			if (entity == null) {
				// The entity event travels on the reliable stream while the transform travels as a
				// datagram: a part may arrive first. Keep it until the entity is created.
				QueueEntityTransform(@event);
				return;
			}

			// The local player drives its own parts: ignore the echo of our own transforms.
			if (entity.IgnoresTransformEcho(@event.SenderId))
				return;

			ApplyPartTransform(entity.GetOrCreatePart(@event.PartRig), @event.Transform);
		}

		/// <summary>Copy the transform fields carried by the event onto a part.</summary>
		private static void ApplyPartTransform(IPart part, TransformObject transform) {
			if (part == null || transform == null)
				return;

			if (transform.Flags.HasFlag(TransformFlags.Position))
				part.Position = transform.GetPosition();
			if (transform.Flags.HasFlag(TransformFlags.Rotation))
				part.Rotation = transform.GetRotation();
			if (transform.Flags.HasFlag(TransformFlags.Scale))
				part.Scale = transform.GetScale();
			if (transform.Flags.HasFlag(TransformFlags.Velocity))
				part.Velocity = transform.GetVelocity();
			if (transform.Flags.HasFlag(TransformFlags.Angular))
				part.Angular = transform.GetAngular();
		}

		/// <summary>Part transforms that arrived before their entity (multipart sync), oldest first.</summary>
		private readonly List<(uint EntityId, TransformEvent Event)> _pending = new();

		/// <summary>Maximum number of buffered part-transform packets: past it, the oldest are dropped.</summary>
		private const int MaxPending = 64;

		private void QueueEntityTransform(TransformEvent @event) {
			// FIFO: the buffer keeps the newest packets, the oldest ones are dropped to make room.
			while (_pending.Count >= MaxPending)
				_pending.RemoveAt(0);

			_pending.Add((@event.EntityId, @event));
		}

		/// <summary>Apply the part transforms buffered for an entity that just got created.</summary>
		internal void FlushPendingEntityTransforms(uint entityId) {
			var entity = _entities.GetEntity<Entity>(entityId);
			if (entity == null)
				return;

			// Oldest first, so the newest transform wins; in place, so the remaining order is kept.
			for (var i = 0; i < _pending.Count;) {
				if (_pending[i].EntityId != entityId) {
					i++;
					continue;
				}

				var pending = _pending[i].Event;
				_pending.RemoveAt(i);
				ApplyPartTransform(entity.GetOrCreatePart(pending.PartRig), pending.Transform);
			}
		}

		/// <summary>Forget the packets buffered for an entity that is gone.</summary>
		internal void DropPendingEntityTransforms(uint entityId)
			=> _pending.RemoveAll(pending => pending.EntityId == entityId);
	}
}
