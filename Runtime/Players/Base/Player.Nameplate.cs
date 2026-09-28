using Nox.CCK.Entities;
using Nox.CCK.Nameplate;
using Nox.Nameplate;
using Keys = Nox.CCK.Nameplate.Constants;

namespace Nox.Relay.Runtime.Players {
	/// <summary>
	/// <see cref="INameplateEntity"/> state of a player, stored in the entity data container so it
	/// stays authoritative and out of reach of the avatar pipeline (anti-cheat).
	/// </summary>
	public abstract partial class Player {
		#region INameplateEntity

		/// <inheritdoc />
		public bool NameplateVisible {
			get => Data.Get("nameplate", EntityConstants.DefaultNameplateVisible);
			set => Data.Set("nameplate", value);
		}

		/// <inheritdoc />
		public bool HealthbarVisible {
			get => Data.Get("healthbar", EntityConstants.DefaultHealthbarVisible);
			set => Data.Set("healthbar", value);
		}

		#endregion

		/// <summary>
		/// Pushes the entity health bar onto a plate (<c>Keys.HEARTS_*</c>): hidden unless
		/// <see cref="HealthbarVisible"/> and a <c>heart</c> value is stored in the entity data —
		/// the data container is the source of truth, the plate only displays it.
		/// </summary>
		internal void PushHealthbar(INameplate plate) {
			if (!plate.IsAlive())
				return;

			if (!HealthbarVisible || !Data.Has("heart")) {
				plate.Set(Keys.HEARTS_VISIBLE, false);
				return;
			}

			plate.Set(Keys.HEARTS_MAX, Data.Get("heart.max", 100f));
			plate.Set(Keys.HEARTS_VALUE, Data.Get("heart", 0f));

			// The value/max push above makes the bar visible: the flag is re-applied last so a
			// hidden bar stays hidden.
			plate.Set(Keys.HEARTS_VISIBLE, true);
		}

		/// <summary>
		/// Pushes the colour of the entity team onto a plate (<c>Keys.COLOR</c>): the colour of the
		/// display name and of the voice image. Cleared back to the default (white) without a team.
		/// </summary>
		internal void PushTeam(INameplate plate) {
			if (!plate.IsAlive())
				return;

			var team = Team;
			plate.Set(Keys.COLOR, team != null ? (object)team.Color : null);
		}
	}
}
