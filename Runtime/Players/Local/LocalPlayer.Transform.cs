using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Nox.CCK.Utils;
using Nox.Relay.Core.Rooms;
using Nox.Relay.Core.Types.Transform;
using UnityEngine;

namespace Nox.Relay.Runtime.Players {
	public partial class LocalPlayer {

		private bool _start;

		private readonly List<(ushort partId, TransformObject delta)> _dirty = new();
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

			var threshold = room.Threshold < 0.0001f ? 0.0001f : room.Threshold;

			foreach (var (index, value) in Parts) {
				if (value is not Part cachedPart)
					continue;
				if (!controller.TryGetPart(index, out var currentTransform))
					continue;
				var deltaTransform = GetChangedTransform(cachedPart, currentTransform, threshold);
				if (deltaTransform == null)
					continue;

				_dirty.Add((partId: index, deltaTransform));

				if (deltaTransform.Flags.HasFlag(TransformFlags.Position))
					cachedPart.SetPosition(deltaTransform.GetPosition());
				if (deltaTransform.Flags.HasFlag(TransformFlags.Rotation))
					cachedPart.SetRotation(deltaTransform.GetRotation());
				if (deltaTransform.Flags.HasFlag(TransformFlags.Scale))
					cachedPart.SetScale(deltaTransform.GetScale());
				if (deltaTransform.Flags.HasFlag(TransformFlags.Velocity))
					cachedPart.SetVelocity(deltaTransform.GetVelocity());
				if (deltaTransform.Flags.HasFlag(TransformFlags.Angular))
					cachedPart.SetAngular(deltaTransform.GetAngular());
			}

			if (_dirty.Count > 0)
				await SendTransformsBatch(room);

		end:
			_start = false;
		}

		/// <summary>
		/// Compares current transform with cached values and builds a delta TransformObject.
		/// </summary>
		private static TransformObject GetChangedTransform(Part cached, TransformObject current, float threshold) {
			var delta = new TransformObject();

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

			return delta.Flags != TransformFlags.None ? delta : null;
		}

		/// <summary>
		/// Sends a batch of transform updates to the room in parallel.
		/// </summary>
		private async UniTask SendTransformsBatch(Room room) {
			_tasks.Clear();

			var time = (uint)(Time.timeAsDouble * 1000.0);

			foreach (var (partId, deltaTransform) in _dirty) {
				var request = TransformRequest.CreateEntity(TransformRequest.SELF, partId, deltaTransform);
				request.SenderTime = time;
				_tasks.Add(room.Transform(request));
			}

			await UniTask.WhenAll(_tasks);
			_tasks.Clear();
		}

	}
}
