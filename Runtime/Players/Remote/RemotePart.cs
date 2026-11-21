using System;
using Nox.CCK.Utils;
using Nox.Entities;
using Nox.Relay.Runtime.Physicals;
using UnityEngine;

namespace Nox.Relay.Runtime.Players {
	/// <summary>
	/// Represents a part of a remote player. Does not interact with controllers.
	/// </summary>
	public class RemotePart : TransformObject, IPart {
		internal RemotePart(RemotePlayer context, ushort id) {
			Id = id;
			Context = context;
		}

		readonly internal RemotePlayer Context;
		public DateTime Updated { get; private set; } = DateTime.UtcNow;

		internal readonly PartTransformBuffer Buffer = new();

		internal void Capture(double time) {
			var tps = Context.Reference?.Room?.Tps ?? 0;

			Buffer.Push(
				time,
				tps > 0 ? 1f / tps : 0f,
				GetPosition(),
				GetRotation(),
				GetScale(),
				GetVelocity(),
				GetAngular()
			);
		}

		public ushort Id { get; }

		/// <summary>Fields of the poses received so far: a field never received is not written to the rig.</summary>
		internal TransformFlags Received { get; private set; } = TransformFlags.None;

		/// <summary>The part is never the one sending its pose, the received fields describe it.</summary>
		public TransformFlags Kind
			=> Received;

		public bool IsDirty {
			get => false;
			set { }
		}

		/// <summary>
		/// The pose received from the owner is stored, then captured for interpolation: the remote player
		/// needs the sender clock the generic copy does not use.
		/// </summary>
		public void Apply(TransformObject transform, uint time = 0) {
			if (transform == null)
				return;

			Received |= transform.Flags;

			if (transform.Flags.HasFlag(TransformFlags.Position))
				Position = transform.GetPosition();
			if (transform.Flags.HasFlag(TransformFlags.Rotation))
				Rotation = transform.GetRotation();
			if (transform.Flags.HasFlag(TransformFlags.Scale))
				Scale = transform.GetScale();
			if (transform.Flags.HasFlag(TransformFlags.Velocity))
				Velocity = transform.GetVelocity();
			if (transform.Flags.HasFlag(TransformFlags.Angular))
				Angular = transform.GetAngular();

			Context.OnPartTransformReceived(Id, time);
		}

		public Vector3 Position {
			get => GetPosition();
			set {
				SetPosition(value);
				Updated = DateTime.UtcNow;
			}
		}

		public Quaternion Rotation {
			get => GetRotation();
			set {
				SetRotation(value);
				Updated = DateTime.UtcNow;
			}
		}

		public Vector3 Scale {
			get => GetScale();
			set {
				SetScale(value);
				Updated = DateTime.UtcNow;
			}
		}

		public Vector3 Velocity {
			get => GetVelocity();
			set {
				SetVelocity(value);
				Updated = DateTime.UtcNow;
			}
		}

		public Vector3 Angular {
			get => GetAngular();
			set {
				SetAngular(value);
				Updated = DateTime.UtcNow;
			}
		}
	}
}