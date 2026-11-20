using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Nox.CCK.Utils;
using Nox.Relay.Core.Rooms;
using Nox.Relay.Core.Types.Transform;
using UnityEngine;

namespace Nox.Relay.Runtime.Players {
	public partial class LocalPlayer {

		private bool _start;

		private readonly List<Part> _dirty = new();
		private readonly List<UniTask<bool>> _tasks = new();

		public override void Tick() {
			base.Tick();

			if (!_start)
				SendTransformsIfNeeded().Forget();
		}

		/// <summary>
		/// Sends transform updates for parts that have moved beyond the threshold.
		/// Called from Tick(), which is already paced by the Room's TPS (or by the frame rate when it
		/// is lower): that is the only limit, there is no per-tick cap here.
		/// </summary>
		async protected UniTask SendTransformsIfNeeded() {
			_start = true;

			var room = Context?.Context.Room;
			if (room == null)
				goto end;

			var controller = Main.ControllerAPI.Current;
			if (controller == null)
				goto end;

			_dirty.Clear();

			var threshold = room.Threshold < 0.0001f
				? 0.0001f
				: room.Threshold;
			var now = DateTime.UtcNow;
			var keyframe = room.PropertyResendInterval;

			foreach (var (index, value) in Parts) {
				if (value is not Part part)
					continue;
				if (!controller.TryGetPart(index, out var current))
					continue;

				var delta = part.Delta;
				delta.Clear();

				var changed = FillChangedTransform(part, current, delta, threshold);

				if (!changed
					&& keyframe > 0
					&& (now - part.Updated).TotalSeconds >= keyframe)
					changed = FillFullTransform(current, delta);

				if (!changed)
					continue;

				_dirty.Add(part);

				if (delta.Flags.HasFlag(TransformFlags.Position))
					part.SetPosition(delta.GetPosition());
				if (delta.Flags.HasFlag(TransformFlags.Rotation))
					part.SetRotation(delta.GetRotation());
				if (delta.Flags.HasFlag(TransformFlags.Scale))
					part.SetScale(delta.GetScale());
				if (delta.Flags.HasFlag(TransformFlags.Velocity))
					part.SetVelocity(delta.GetVelocity());
				if (delta.Flags.HasFlag(TransformFlags.Angular))
					part.SetAngular(delta.GetAngular());
			}

			if (_dirty.Count > 0)
				await SendTransformsBatch(room);

		end:
			_start = false;
		}

		/// <summary>
		/// Fills <paramref name="delta"/> with the values that changed beyond the threshold.
		/// Returns <c>false</c> when nothing changed (the delta is then left empty).
		/// </summary>
		private static bool FillChangedTransform(TransformObject cached, TransformObject current, TransformObject delta, float threshold) {
			if (current.Flags.HasFlag(TransformFlags.Position) && !current.IsSamePosition(cached.GetPosition(), threshold))
				delta.SetPosition(current.GetPosition());

			if (current.Flags.HasFlag(TransformFlags.Rotation) && !current.IsSameRotation(cached.GetRotation(), threshold))
				delta.SetRotation(current.GetRotation());

			if (current.Flags.HasFlag(TransformFlags.Scale) && !current.IsSameScale(cached.GetScale(), threshold))
				delta.SetScale(current.GetScale());

			if (current.Flags.HasFlag(TransformFlags.Velocity) && !current.IsSameVelocity(cached.GetVelocity(), threshold))
				delta.SetVelocity(current.GetVelocity());

			if (current.Flags.HasFlag(TransformFlags.Angular) && !current.IsSameAngular(cached.GetAngular(), threshold))
				delta.SetAngular(current.GetAngular());

			return delta.Flags != TransformFlags.None;
		}

		/// <summary>
		/// Fills <paramref name="delta"/> with every value the controller provides, for the periodic
		/// keyframe: a peer that missed an update gets the complete pose instead of a delta it cannot
		/// apply. Returns <c>false</c> when the controller provides nothing.
		/// </summary>
		private static bool FillFullTransform(TransformObject current, TransformObject delta) {
			if (current.Flags.HasFlag(TransformFlags.Position))
				delta.SetPosition(current.GetPosition());
			if (current.Flags.HasFlag(TransformFlags.Rotation))
				delta.SetRotation(current.GetRotation());
			if (current.Flags.HasFlag(TransformFlags.Scale))
				delta.SetScale(current.GetScale());
			if (current.Flags.HasFlag(TransformFlags.Velocity))
				delta.SetVelocity(current.GetVelocity());
			if (current.Flags.HasFlag(TransformFlags.Angular))
				delta.SetAngular(current.GetAngular());

			return delta.Flags != TransformFlags.None;
		}

		/// <summary>
		/// Sends a batch of transform updates to the room in parallel.
		/// </summary>
		private async UniTask SendTransformsBatch(Room room) {
			_tasks.Clear();

			var time = (uint)(Time.timeAsDouble * 1000.0);

			foreach (var part in _dirty) {
				var request = TransformRequest.CreateEntity(TransformRequest.SELF, part.Id, part.Delta);
				request.SenderTime = time;
				_tasks.Add(room.Transform(request));
			}

			if (_tasks.Count == 1)
				await _tasks[0];
			else
				await UniTask.WhenAll(_tasks);

			_tasks.Clear();
		}

	}
}
