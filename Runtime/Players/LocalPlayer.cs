using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Nox.CCK.Utils;
using Nox.Controllers;
using Nox.Relay.Core.Rooms;
using Nox.Relay.Core.Types.Transform;
using CorePlayer = Nox.Relay.Core.Players.Player;

namespace Nox.Relay.Runtime.Players {
	public class LocalPlayer : Player {
		public LocalPlayer(Entities context, CorePlayer player) : base(context, player) { }

		public override bool IsLocal => true;

		public override void Update() {
			// Disabled for local player - no interpolation needed
		}

		public override void Tick() {
			base.Tick();

			// Check if any parts need transform updates and send them
			// Note: Tick() is already rate-limited by the Room's TPS via Session.Update()
			if (!_startTransform)
				SendTransformsIfNeeded().Forget();
		}

		// Prevents concurrent execution of SendTransformsIfNeeded()
		private bool _startTransform = false;

		internal void UpdateController(IController controller) {
			if (controller == null) {
				RemoveController();
				return;
			}

			var cParts = controller.GetParts();

			// Remove parts that no longer exist in the controller
			var keysToRemove = Parts.Keys.Except(cParts.Select(p => p.Key)).ToList();
			foreach (var key in keysToRemove)
				Parts.Remove(key);

			// Add new parts from the controller (initialize cache)
			foreach (var cPart in cParts) {
				if (Parts.ContainsKey(cPart.Key)) continue;
				Parts[cPart.Key] = new Part(this, cPart.Key);
			}

			// Initialize each part's cache with current controller values
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
		/// Compares current controller values against cached (last-sent) values in Parts.
		/// Updates the cache immediately after detection to prevent duplicate sends.
		/// </summary>
		private async UniTask SendTransformsIfNeeded() {
			_startTransform = true;

			var room = Context?.Context.Room;
			if (room == null) goto end;

			// Need active controller to read current transform values
			var controller = Main.ControllerAPI.Current;
			if (controller == null) goto end;

			// Collect parts that need updates (moved beyond threshold)
			var dirtyParts = new List<(ushort partId, TransformObject delta)>();
			
			// Ensure a sensible minimum threshold to avoid floating point precision issues
			var threshold = room.Threshold < 0.0001f ? 0.0001f : room.Threshold;

			foreach (var (index, value) in Parts) {
				if (value is not Part cachedPart) continue;

				// Get current transform from controller
				if (!controller.TryGetPart(index, out var currentTransform)) continue;

				// Compare current vs cached and get only changed values exceeding threshold
				var deltaTransform = GetChangedTransform(cachedPart, currentTransform, threshold);
				if (deltaTransform == null) continue;
				
				dirtyParts.Add((partId: index, deltaTransform));

				// Update cache with values we're about to send
				// This prevents re-sending the same delta on the next Tick()
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
			return;
		}

		/// <summary>
		/// Compares current transform with cached (last-sent) values and builds a delta TransformObject
		/// containing ONLY the properties that have changed beyond the threshold.
		/// Returns null if nothing has changed significantly.
		/// </summary>
		/// <param name="cached">Cached part containing the last-sent transform values</param>
		/// <param name="current">Current transform from the controller</param>
		/// <param name="threshold">Distance/magnitude threshold for detecting changes (Unity units)</param>
		/// <returns>Delta TransformObject with only changed properties, or null if no changes</returns>
		private static TransformObject GetChangedTransform(Part cached, TransformObject current, float threshold) {
			var delta = new TransformObject();

			// Compare position and add to delta if changed beyond threshold
			if (current.Flags.HasFlag(TransformFlags.Position) && !current.IsSamePosition(cached.GetPosition(), threshold))
				delta.SetPosition(current.GetPosition());

			// Compare rotation and add to delta if changed beyond threshold
			if (current.Flags.HasFlag(TransformFlags.Rotation) && !current.IsSameRotation(cached.GetRotation(), threshold)) 
				delta.SetRotation(current.GetRotation());

			// Compare scale and add to delta if changed beyond threshold
			if (current.Flags.HasFlag(TransformFlags.Scale) && !current.IsSameScale(cached.GetScale(), threshold))
				delta.SetScale(current.GetScale());

			// Compare velocity and add to delta if changed beyond threshold
			if (current.Flags.HasFlag(TransformFlags.Velocity) && !current.IsSameVelocity(cached.GetVelocity(), threshold))
				delta.SetVelocity(current.GetVelocity());

			// Compare angular velocity and add to delta if changed beyond threshold
			if (current.Flags.HasFlag(TransformFlags.Angular) && !current.IsSameAngular(cached.GetAngular(), threshold))
				delta.SetAngular(current.GetAngular());

			// Return delta only if at least one property changed
			return delta.Flags != TransformFlags.None
				? delta
				: null;
		}

		/// <summary>
		/// Sends a batch of transform updates to the room in parallel.
		/// Each part is sent individually (as required by the relay protocol).
		/// All requests are initiated without blocking, then awaited together for efficiency.
		/// </summary>
		/// <param name="room">The room to send transforms to</param>
		/// <param name="dirtyParts">List of parts with their delta transforms to send</param>
		private static async UniTask SendTransformsBatch(Room room, List<(ushort partId, TransformObject delta)> dirtyParts) {
			// Initiate all transform requests without blocking
			// This allows parallel network operations for better performance
			var tasks = new List<UniTask<bool>>();

			foreach (var (partId, deltaTransform) in dirtyParts) {
				var request = TransformRequest.CreateEntity(ushort.MaxValue, partId, deltaTransform);
				tasks.Add(room.Transform(request));
			}

			// Wait for all network operations to complete
			await UniTask.WhenAll(tasks);
		}
	}
}