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
			var entity = _entities.GetEntity(@event.EntityId);
			if (entity == null) {
				QueueEntityTransform(@event);
				return;
			}

			if (@event.SenderId != 0 && @event.SenderId == _entities.LocalId)
				return;

			ApplyPartTransform(entity, @event.PartRig, @event.Transform, @event.SenderTime);
		}

		/// <summary>Pose reused for every part transform applied: the values are copied in before use.</summary>
		private readonly TransformObject _pose = new();

		/// <summary>Copy the transform fields carried by the event onto a part.</summary>
		private void ApplyPartTransform(IEntity entity, ushort partId, TransformObject transform, uint time) {
			if (transform == null || entity is not IMultiPartEntity<IPart> multi)
				return;

			var pose = _pose;
			pose.Clear();

			if (transform.Flags.HasFlag(TransformFlags.Position))
				pose.SetPosition(transform.GetPosition());
			if (transform.Flags.HasFlag(TransformFlags.Rotation))
				pose.SetRotation(transform.GetRotation());
			if (transform.Flags.HasFlag(TransformFlags.Scale))
				pose.SetScale(transform.GetScale());
			if (transform.Flags.HasFlag(TransformFlags.Velocity))
				pose.SetVelocity(transform.GetVelocity());
			if (transform.Flags.HasFlag(TransformFlags.Angular))
				pose.SetAngular(transform.GetAngular());

			multi.ApplyPart(partId, pose, time);
		}

		/// <summary>Kind of the pose sent, from the flags the relay carries.</summary>
		/// <summary>Part transforms that arrived before their entity (multipart sync), oldest first.</summary>
		private readonly List<(uint EntityId, TransformEvent Event)> _pending = new();

		/// <summary>Maximum number of buffered part-transform packets: past it, the oldest are dropped.</summary>
		private const int MAX_PENDING = 64;

		private void QueueEntityTransform(TransformEvent @event) {
			while (_pending.Count >= MAX_PENDING)
				_pending.RemoveAt(0);

			_pending.Add((@event.EntityId, @event));
		}

		/// <summary>Apply the part transforms buffered for an entity that just got created.</summary>
		internal void FlushPendingEntityTransforms(uint entityId) {
			var entity = _entities.GetEntity(entityId);
			if (entity == null)
				return;

			for (var i = 0; i < _pending.Count;) {
				if (_pending[i].EntityId != entityId) {
					i++;
					continue;
				}

				var pending = _pending[i].Event;
				_pending.RemoveAt(i);
				ApplyPartTransform(entity, pending.PartRig, pending.Transform, pending.SenderTime);
			}
		}

		/// <summary>Forget the packets buffered for an entity that is gone.</summary>
		internal void DropPendingEntityTransforms(uint entityId)
			=> _pending.RemoveAll(pending => pending.EntityId == entityId);
	}
}
