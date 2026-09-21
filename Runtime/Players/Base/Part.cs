using System;
using Nox.CCK.Utils;
using Nox.Controllers;
using Nox.Entities;
using UnityEngine;

namespace Nox.Relay.Runtime.Players {
	public class Part : TransformObject, IPart {
		// if the controller is null, the new values are stored but not applied
		private IController _controller;

		/// <summary>
		/// The bound controller, or null when none is bound or the bound instance has been
		/// destroyed.
		/// Unity's fake-null only applies to <see cref="UnityEngine.Object"/> references: a
		/// destroyed proxy reached through <see cref="IController"/> still compares unequal to
		/// null, so every member below would otherwise hit a destroyed object and throw
		/// MissingReferenceException until the part is rebound.
		/// </summary>
		private IController ActiveController {
			get {
				if (_controller is UnityEngine.Object o && !o)
					_controller = null;
				return _controller;
			}
		}

		public DateTime Updated { get; private set; } = DateTime.UtcNow;

		internal void Restore(IController controller) {
			if (_controller == controller) return;

			// restore stored values
			if (Flags != TransformFlags.None)
				controller.SetPart(Id, this);

			_controller = controller;
		}

		internal void Store() {
			// store current values
			var controller = ActiveController;
			if (controller != null && controller.TryGetPart(Id, out var part)) {
				if (!IsSamePosition(part.GetPosition()))
					SetPosition(part.GetPosition());
				if (!IsSameRotation(part.GetRotation()))
					SetRotation(part.GetRotation());
				if (!IsSameScale(part.GetScale()))
					SetScale(part.GetScale());
				if (!IsSameVelocity(part.GetVelocity()))
					SetVelocity(part.GetVelocity());
				if (!IsSameAngular(part.GetAngular()))
					SetAngular(part.GetAngular());
				Updated = DateTime.UtcNow;
			}

			_controller = null;
		}

		internal Part(LocalPlayer context, ushort id) {
			Id = id;
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
				=> ActiveController != null && ActiveController.TryGetPart(Id, out var part)
					? part.GetPosition()
					: GetPosition();
			set {
				if (ActiveController != null) {
					var part = new TransformObject();
					part.SetPosition(value);
					Updated = DateTime.UtcNow;
					ActiveController.SetPart(Id, part);
				}

				SetPosition(value);
				Updated = DateTime.UtcNow;
			}
		}

		public Quaternion Rotation {
			get
				=> ActiveController != null && ActiveController.TryGetPart(Id, out var part)
					? part.GetRotation()
					: GetRotation();
			set {
				if (ActiveController != null) {
					var part = new TransformObject();
					part.SetRotation(value);
					Updated = DateTime.UtcNow;
					ActiveController.SetPart(Id, part);
				}

				SetRotation(value);
				Updated = DateTime.UtcNow;
			}
		}

		public Vector3 Scale {
			get
				=> ActiveController != null && ActiveController.TryGetPart(Id, out var part)
					? part.GetScale()
					: GetScale();
			set {
				if (ActiveController != null) {
					var part = new TransformObject();
					part.SetScale(value);
					Updated = DateTime.UtcNow;
					ActiveController.SetPart(Id, part);
				}

				SetScale(value);
				Updated = DateTime.UtcNow;
			}
		}

		public Vector3 Velocity {
			get
				=> ActiveController != null && ActiveController.TryGetPart(Id, out var part)
					? part.GetVelocity()
					: GetVelocity();
			set {
				if (ActiveController != null) {
					var part = new TransformObject();
					part.SetVelocity(value);
					Updated = DateTime.UtcNow;
					ActiveController.SetPart(Id, part);
				}

				SetVelocity(value);
				Updated = DateTime.UtcNow;
			}
		}

		public Vector3 Angular {
			get
				=> ActiveController != null && ActiveController.TryGetPart(Id, out var part)
					? part.GetAngular()
					: GetAngular();
			set {
				if (ActiveController != null) {
					var part = new TransformObject();
					part.SetAngular(value);
					Updated = DateTime.UtcNow;
					ActiveController.SetPart(Id, part);
				}

				SetAngular(value);
				Updated = DateTime.UtcNow;
			}
		}
	}
}