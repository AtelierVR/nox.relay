using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Nox.CCK.Utils;
using Nox.Controllers;
using Nox.Relay.Core.Rooms;
using Nox.Relay.Core.Types.Transform;
using UnityEngine;
using CorePlayer = Nox.Relay.Core.Players.Player;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Relay.Runtime.Players {
	public class LocalPlayer : Player {
		public LocalPlayer(Entities context, CorePlayer player) : base(context, player) { }

		public override bool IsLocal => true;

		public override void Update() {
			// disabled for local player
		}

		public override void Tick() {
			base.Tick();

			// Send transforms for parts that have moved beyond threshold
			// Note: Tick() is already called at the Room's TPS rate by Session.Update()
			if (!_startTransform)
				SendTransformsIfNeeded().Forget();
		}

		private bool _startTransform = false;

		internal void UpdateController(IController controller) {
			if (controller == null) {
				RemoveController();
				return;
			}

			var cParts = controller.GetParts();

			// remove parts not in controller
			var keysToRemove = Parts.Keys.Except(cParts.Select(p => p.Key)).ToList();
			foreach (var key in keysToRemove)
				Parts.Remove(key);

			// add parts from controller
			foreach (var cPart in cParts) {
				if (Parts.ContainsKey(cPart.Key)) continue;
				Parts[cPart.Key] = new Part(this, cPart.Key);
			}

			// restore parts
			foreach (var part in Parts.Values)
				if (part is Part p)
					p.Restore(controller);
		}

		internal void RemoveController() {
			foreach (var part in Parts.Values)
				if (part is Part p)
					p.Store();
		}

		/// <summary>
		/// Sends transform updates for parts that have moved beyond the threshold.
		/// Called from Tick() which is already rate-limited by the Room's TPS.
		/// Uses the controller for current values and Parts for last sent values (cache).
		/// </summary>
		private async UniTask SendTransformsIfNeeded() {
			_startTransform = true;

			var room = Context?.Context.Room;
			if (room == null) goto end;

			// Need active controller to read current values
			var controller = Main.ControllerAPI.Current;
			if (controller == null) goto end;

			// Collect parts that need to be sent based on threshold
			var dirtyParts = new List<(ushort partId, TransformObject delta)>();
			// Use a minimum threshold to avoid floating point precision issues
			// If room.Threshold is too small (like float.Epsilon), use a sensible minimum
			var threshold = room.Threshold < 0.0001f ? 0.0001f : room.Threshold;

			foreach (var (index, value) in Parts) {
				if (value is not Part cachedPart) continue;

				// Get current values from controller
				if (!controller.TryGetPart(index, out var currentTransform)) continue;

				// Get only the changed values that exceed threshold
				var deltaTransform = GetChangedTransform(cachedPart, currentTransform, threshold);
				if (deltaTransform == null) continue;
				dirtyParts.Add((partId: index, deltaTransform));

				// Update cached part with new sent values	
				// so we don't resend the same values next time
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

			// Send all dirty parts in batch
			if (dirtyParts.Count > 0)
				await SendTransformsBatch(room, dirtyParts);

			end:
			_startTransform = false;
			return;
		}

		/// <summary>
		/// Compares current transform with cached values and returns a TransformObject
		/// containing ONLY the values that have changed beyond the threshold.
		/// Returns null if nothing has changed.
		/// </summary>
		/// <param name="cached">Cached part with last sent values</param>
		/// <param name="current">Current transform from controller</param>
		/// <param name="threshold">Threshold for position, scale, velocities (in Unity units)</param>
		private static TransformObject GetChangedTransform(Part cached, TransformObject current, float threshold) {
			var delta = new TransformObject();

			// Check and add only changed position
			if (current.Flags.HasFlag(TransformFlags.Position) && !current.IsSamePosition(cached.GetPosition(), threshold))
				delta.SetPosition(current.GetPosition());

			// Check and add only changed rotation
			if (current.Flags.HasFlag(TransformFlags.Rotation) && !current.IsSameRotation(cached.GetRotation(), threshold)) {
				Logger.Log(Quaternion.Angle(current.GetRotation(), cached.GetRotation()));
				delta.SetRotation(current.GetRotation());
			}

			// Check and add only changed scale
			if (current.Flags.HasFlag(TransformFlags.Scale) && !current.IsSameScale(cached.GetScale(), threshold))
				delta.SetScale(current.GetScale());

			// Check and add only changed velocity
			if (current.Flags.HasFlag(TransformFlags.Velocity) && !current.IsSameVelocity(cached.GetVelocity(), threshold))
				delta.SetVelocity(current.GetVelocity());

			// Check and add only changed angular velocity
			if (current.Flags.HasFlag(TransformFlags.Angular) && !current.IsSameAngular(cached.GetAngular(), threshold))
				delta.SetAngular(current.GetAngular());

			// If no changes, return null
			return delta.Flags != TransformFlags.None
				? delta
				: null;
		}

		/// <summary>
		/// Sends a batch of transform updates to the room.
		/// Only sends the changed values (delta) for each part.
		/// Updates the cache BEFORE sending to prevent race conditions with Tick().Forget().
		/// </summary>
		private static async UniTask SendTransformsBatch(Room room, List<(ushort partId, TransformObject delta)> dirtyParts) {
			// Send each part individually (the relay protocol requires per-part requests)
			// But we can optimize by sending them without awaiting each one
			var tasks = new List<UniTask<bool>>();

			foreach (var (partId, deltaTransform) in dirtyParts) {
				var request = TransformRequest.CreateEntity(ushort.MaxValue, partId, deltaTransform);
				tasks.Add(room.Transform(request));
			}

			// Wait for all sends to complete
			await UniTask.WhenAll(tasks);
		}
	}
}