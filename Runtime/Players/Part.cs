using Nox.CCK.Utils;
using Nox.Controllers;
using Nox.Entities;
using UnityEngine;

namespace Nox.Relay.Runtime.Players {
	public class Part : TransformObject, IPart {
		// if the controller is null, the new values are stored but not applied
		private IController _controller;

		internal void Restore(IController controller) {
			if (_controller == controller) return;

			// restore stored values
			if (Flags != TransformFlags.None)
				controller.SetPart(Id, this);

			_controller = controller;
		}

		internal void Store() {
			// store current values
			if (_controller != null && _controller.TryGetPart(Id, out var part)) {
				if (!IsSamePosition(part.GetPosition()))
					SetPosition(part.GetPosition());
				if (!IsSameRotation(part.GetRotation()))
					SetRotation(part.GetRotation());
				if (!IsSameScale(part.GetScale()))
					SetScale(part.GetScale());
				if (!IsSameVelocity(part.GetVelocity()))
					SetVelocity(part.GetVelocity());
				if (!IsSameAngularVelocity(part.GetAngularVelocity()))
					SetAngular(part.GetAngularVelocity());
			}

			_controller = null;
		}

		internal Part(LocalPlayer context, ushort id) {
			Id      = id;
			Context = context;
		}

		internal readonly LocalPlayer Context;

		public ushort Id { get; }

		public bool IsDirty {
			get => false;
			set { }
		}

		public Vector3 Position {
			get
				=> _controller != null && _controller.TryGetPart(Id, out var part)
					? part.GetPosition()
					: GetPosition();
			set {
				if (_controller != null) {
					var part = new TransformObject();
					part.SetPosition(value);
					_controller.SetPart(Id, part);
				}

				SetPosition(value);
			}
		}

		public Quaternion Rotation {
			get
				=> _controller != null && _controller.TryGetPart(Id, out var part)
					? part.GetRotation()
					: GetRotation();
			set {
				if (_controller != null) {
					var part = new TransformObject();
					part.SetRotation(value);
					_controller.SetPart(Id, part);
				}

				SetRotation(value);
			}
		}

		public Vector3 Scale {
			get
				=> _controller != null && _controller.TryGetPart(Id, out var part)
					? part.GetScale()
					: GetScale();
			set {
				if (_controller != null) {
					var part = new TransformObject();
					part.SetScale(value);
					_controller.SetPart(Id, part);
				}

				SetScale(value);
			}
		}

		public Vector3 Velocity {
			get
				=> _controller != null && _controller.TryGetPart(Id, out var part)
					? part.GetVelocity()
					: GetVelocity();
			set {
				if (_controller != null) {
					var part = new TransformObject();
					part.SetVelocity(value);
					_controller.SetPart(Id, part);
				}

				SetVelocity(value);
			}
		}

		public Vector3 Angular {
			get
				=> _controller != null && _controller.TryGetPart(Id, out var part)
					? part.GetAngularVelocity()
					: GetAngularVelocity();
			set {
				if (_controller != null) {
					var part = new TransformObject();
					part.SetAngular(value);
					_controller.SetPart(Id, part);
				}

				SetAngular(value);
			}
		}
	}
}