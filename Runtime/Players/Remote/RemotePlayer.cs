using Nox.CCK.Players;
using Nox.Entities;
using Nox.Relay.Runtime.Voice;
using CorePlayer = Nox.Relay.Core.Players.Player;

namespace Nox.Relay.Runtime.Players {
	/// <summary>
	/// Remote (networked) player. The class is split across partials:
	/// avatar handling in <c>RemotePlayer.Avatar.cs</c>, physical representation in
	/// <c>RemotePlayer.Physical.cs</c>.
	/// </summary>
	public partial class RemotePlayer : Player {
		public RemotePlayer(Entities context, CorePlayer player) : base(context, player) {
			// Initialize default parts for a basic player rig
			InitializeDefaultParts();
			VoiceProvider = new RemoteVoiceProvider(this);
		}

		protected override IPart CreatePart(ushort index)
			=> new RemotePart(this, index);

		public override bool IsLocal
			=> false;

		private void InitializeDefaultParts() {
			// Create base part at minimum
			if (!Parts.ContainsKey(PlayerRig.Base.ToIndex()))
				Parts[PlayerRig.Base.ToIndex()] = new RemotePart(this, PlayerRig.Base.ToIndex());
		}

		/// <summary>
		/// Update or create a part for this remote player
		/// </summary>
		internal void UpdatePart(ushort partId, IPart partData) {
			if (!Parts.TryGetValue(partId, out var part)) {
				part          = new RemotePart(this, partId);
				Parts[partId] = part;
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