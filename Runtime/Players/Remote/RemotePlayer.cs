using System;
using Nox.CCK.Entities;
using Nox.Entities;
using UnityEngine;
using CorePlayer = Nox.Relay.Core.Players.Player;

namespace Nox.Relay.Runtime.Players {
	/// <summary>
	/// Remote (networked) player. The class is split across partials:
	/// avatar handling in <c>RemotePlayer.Avatar.cs</c>, physical representation in
	/// <c>RemotePlayer.Physical.cs</c> and voice in <c>RemotePlayer.Voice.cs</c>.
	/// </summary>
	public partial class RemotePlayer : Player {
		public RemotePlayer(Entities context, CorePlayer player) : base(context, player)
			=> InitializeDefaultParts();

		protected override IPart CreatePart(ushort index)
			=> new RemotePart(this, index);

		private const double CLOCK_SMOOTHING = 0.05;
		private const double CLOCK_RESYNC_THRESHOLD = 1.0;

		private double _clockOffset;
		private bool _hasClock;

		protected internal override void OnPartTransformReceived(ushort partId, uint time) {
			if (!Parts.TryGetValue(partId, out var part) || part is not RemotePart remote)
				return;

			var now = Time.timeAsDouble;
			if (time == 0) {
				remote.Capture(now);
				return;
			}

			var sample = now - time * 0.001;
			if (!_hasClock || Math.Abs(sample - _clockOffset) > CLOCK_RESYNC_THRESHOLD) {
				_clockOffset = sample;
				_hasClock = true;
			} else {
				_clockOffset += (sample - _clockOffset) * CLOCK_SMOOTHING;
			}

			remote.Capture(time * 0.001 + _clockOffset);
		}

		public override bool IsLocal
			=> false;

		private void InitializeDefaultParts() {
			if (!Parts.ContainsKey(EntityRig.Base.ToIndex()))
				Parts[EntityRig.Base.ToIndex()] = new RemotePart(this, EntityRig.Base.ToIndex());
		}

		/// <summary>
		/// Update or create a part for this remote player
		/// </summary>
		internal void UpdatePart(ushort partId, IPart partData) {
			if (!Parts.TryGetValue(partId, out var part)) {
				part          = new RemotePart(this, partId);
				Parts[partId] = part;
			}

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
