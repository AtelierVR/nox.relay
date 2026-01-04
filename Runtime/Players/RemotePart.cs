using Nox.CCK.Utils;
using Nox.Entities;
using UnityEngine;

namespace Nox.Relay.Runtime.Players {
	/// <summary>
	/// Represents a part of a remote player. Does not interact with controllers.
	/// </summary>
	public class RemotePart : TransformObject, IPart {
		internal RemotePart(RemotePlayer context, ushort id) {
			Id      = id;
			Context = context;
		}

		internal readonly RemotePlayer Context;

		public ushort Id { get; }

		public bool IsDirty {
			get => false;
			set { }
		}

		public Vector3 Position {
			get => GetPosition();
			set => SetPosition(value);
		}

		public Quaternion Rotation {
			get => GetRotation();
			set => SetRotation(value);
		}

		public Vector3 Scale {
			get => GetScale();
			set => SetScale(value);
		}

		public Vector3 Velocity {
			get => GetVelocity();
			set => SetVelocity(value);
		}

		public Vector3 Angular {
			get => GetAngularVelocity();
			set => SetAngular(value);
		}
	}
}

