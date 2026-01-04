using System.Linq;
using Nox.CCK.Players;
using Nox.Entities;
using CorePlayer = Nox.Relay.Core.Players.Player;

namespace Nox.Relay.Runtime.Players {
	public class RemotePlayer : Player {
		public RemotePlayer(Entities context, CorePlayer player) : base(context, player) {
			// Initialize default parts for a basic player rig
			InitializeDefaultParts();
		}

		public override bool IsLocal => false;

		private void InitializeDefaultParts() {
			// Create base part at minimum
			if (!_parts.ContainsKey(PlayerRig.Base.ToIndex()))
				_parts[PlayerRig.Base.ToIndex()] = new RemotePart(this, PlayerRig.Base.ToIndex());
		}

		/// <summary>
		/// Update or create a part for this remote player
		/// </summary>
		internal void UpdatePart(ushort partId, IPart partData) {
			if (!_parts.TryGetValue(partId, out var part)) {
				part = new RemotePart(this, partId);
				_parts[partId] = part;
			}

			// Update the part's transform data
			if (partData.Position != part.Position)
				part.Position = partData.Position;
			if (partData.Rotation != part.Rotation)
				part.Rotation = partData.Rotation;
			if (partData.Scale != part.Scale)
				part.Scale = partData.Scale;
			if (partData.Velocity != part.Velocity)
				part.Velocity = partData.Velocity;
			if (partData.Angular != part.Angular)
				part.Angular = partData.Angular;
		}
	}
}

