using System;
using Nox.CCK.Utils;
using Nox.Entities;
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

		public ushort Id { get; }

		public bool IsDirty {
			get => false;
			set { }
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