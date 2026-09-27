using Nox.CCK.Utils;
using Nox.Relay.Core.Types.Transform;
using Nox.Relay.Runtime.Players;
using Logger = Nox.CCK.Utils.Logger;
using Player = Nox.Relay.Runtime.Players.Player;

namespace Nox.Relay.Runtime {
	/// <summary>Transform synchronization (player parts and object paths).</summary>
	public sealed partial class Session {
		internal void OnTransformHandler(TransformEvent @event) {
			if (@event.Type == TransformType.EntityPart) {
				// Handle player part transformation
				var player = InterEntities.GetEntity<Player>(@event.EntityId);
				if (player == null) {
					Logger.LogWarning($"Player with ID {@event.EntityId} not found for Transform event", tag: Tag);
					return;
				}

				// Update the remote player part if it's a remote player
				if (player is RemotePlayer remotePlayer) {
					// Get or create the part
					if (!remotePlayer.TryGetPart(@event.PartRig, out var part)) {
						Logger.LogDebug($"Creating new part {@event.PartRig} for remote player {@event.EntityId}", tag: Tag);
						part                               = new RemotePart(remotePlayer, @event.PartRig);
						remotePlayer.Parts[@event.PartRig] = part;
					}

					// Apply transform data from the event
					var transform = @event.Transform;
					if (transform.Flags.HasFlag(TransformFlags.Position))
						part.Position = transform.GetPosition();
					if (transform.Flags.HasFlag(TransformFlags.Rotation))
						part.Rotation = transform.GetRotation();
					if (transform.Flags.HasFlag(TransformFlags.Scale))
						part.Scale = transform.GetScale();
					if (transform.Flags.HasFlag(TransformFlags.Velocity))
						part.Velocity = transform.GetVelocity();
					if (transform.Flags.HasFlag(TransformFlags.Angular))
						part.Angular = transform.GetAngular();
				} else if (player is LocalPlayer localPlayer) {
					// Apply server-directed transform updates (e.g. server teleport/respawn)
					if (@event.SenderId != player.Id) {
						var part = localPlayer.GetOrCreatePart(@event.PartRig);
						var transform = @event.Transform;
						if (transform.Flags.HasFlag(TransformFlags.Position))
							part.Position = transform.GetPosition();
						if (transform.Flags.HasFlag(TransformFlags.Rotation))
							part.Rotation = transform.GetRotation();
						if (transform.Flags.HasFlag(TransformFlags.Scale))
							part.Scale = transform.GetScale();
						if (transform.Flags.HasFlag(TransformFlags.Velocity))
							part.Velocity = transform.GetVelocity();
						if (transform.Flags.HasFlag(TransformFlags.Angular))
							part.Angular = transform.GetAngular();
					}
				}
			} else if (@event.Type == TransformType.ByPath) {
				// Handle object transformation by path
				Logger.LogWarning($"Transform by path not yet implemented: Path={@event.Path}", tag: Tag);
				// TODO: Implement path-based transform when needed
			} else {
				Logger.LogWarning($"Unknown transform type: {@event.Type}", tag: Tag);
			}
		}
	}
}
