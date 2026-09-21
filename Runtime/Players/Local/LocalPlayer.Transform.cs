using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Nox.CCK.Utils;
using Nox.Relay.Core.Rooms;
using Nox.Relay.Core.Types.Transform;

namespace Nox.Relay.Runtime.Players {
	public partial class LocalPlayer {

		// Prevents concurrent execution of SendTransformsIfNeeded()
		private bool _startTransform;

		public override void Tick() {
			base.Tick();

			if (!_startTransform)
				SendTransformsIfNeeded().Forget();
		}

		/// <summary>
		/// Sends transform updates for parts that have moved beyond the threshold.
		/// Called from Tick() which is already rate-limited by the Room's TPS.
		/// </summary>
		async protected UniTask SendTransformsIfNeeded() {
			_startTransform = true;

			var room = Context?.Context.Room;
			if (room == null)
				goto end;

			// Need active controller to read current transform values
			var controller = Main.ControllerAPI.Current;
			if (controller == null)
				goto end;

			// Collect parts that need updates (moved beyond threshold)
			var dirtyParts = new List<(ushort partId, TransformObject delta)>();

			// Ensure a sensible minimum threshold to avoid floating point precision issues
			var threshold = room.Threshold < 0.0001f ? 0.0001f : room.Threshold;

			foreach (var (index, value) in Parts) {
				if (value is not Part cachedPart)
					continue;

				// Get current transform from controller
				if (!controller.TryGetPart(index, out var currentTransform))
					continue;

				// Compare current vs cached and get only changed values exceeding threshold
				var deltaTransform = GetChangedTransform(cachedPart, currentTransform, threshold);
				if (deltaTransform == null)
					continue;

				dirtyParts.Add((partId: index, deltaTransform));

				// Update cache with values we're about to send
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

			// Send all changes in a batch
			if (dirtyParts.Count > 0)
				await SendTransformsBatch(room, dirtyParts);

		end:
			_startTransform = false;
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
		private static async UniTask SendTransformsBatch(Room room, List<(ushort partId, TransformObject delta)> dirtyParts) {
			var tasks = new List<UniTask<bool>>();

			foreach (var (partId, deltaTransform) in dirtyParts) {
				var request = TransformRequest.CreateEntity(ushort.MaxValue, partId, deltaTransform);
				tasks.Add(room.Transform(request));
			}

			await UniTask.WhenAll(tasks);
		}

	}
}
