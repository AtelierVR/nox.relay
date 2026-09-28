using Nox.CCK.Entities;
using Nox.Entities;

namespace Nox.Relay.Runtime.Players {
	/// <summary>
	/// <see cref="IMovingEntity"/> state of a player, stored in the entity data container under the
	/// controller ability keys (<c>Nox.Controllers.AbilitiesConstants</c>) so it stays authoritative
	/// and out of reach of the avatar pipeline (anti-cheat).
	/// </summary>
	public abstract partial class Player {
		#region IMovingEntity

		/// <inheritdoc />
		public float WalkSpeed {
			get => Data.Get("max_move_speed", EntityConstants.DefaultWalkSpeed);
			set => Data.Set("max_move_speed", value);
		}

		/// <inheritdoc />
		public float MoveAcceleration {
			get => Data.Get("move_acceleration", EntityConstants.DefaultMoveAcceleration);
			set => Data.Set("move_acceleration", value);
		}

		/// <inheritdoc />
		public float JumpForce {
			get => Data.Get("jump_force", EntityConstants.DefaultJumpForce);
			set => Data.Set("jump_force", value);
		}

		/// <inheritdoc />
		public float SprintMultiplier {
			get => Data.Get("sprint_multiplier", EntityConstants.DefaultSprintMultiplier);
			set => Data.Set("sprint_multiplier", value);
		}

		/// <inheritdoc />
		public float AirControl {
			get => Data.Get("air_control", EntityConstants.DefaultAirControl);
			set => Data.Set("air_control", value);
		}

		/// <inheritdoc />
		public float FlySpeed {
			get => Data.Get("fly_speed", EntityConstants.DefaultFlySpeed);
			set => Data.Set("fly_speed", value);
		}

		/// <inheritdoc />
		public bool MayFly {
			get => Data.Get("may_fly", EntityConstants.DefaultMayFly);
			set => Data.Set("may_fly", value);
		}

		/// <inheritdoc />
		public bool IsImmobilized {
			get => Data.Get("immobilized", EntityConstants.DefaultImmobilized);
			set => Data.Set("immobilized", value);
		}

		/// <inheritdoc />
		public bool IsFlying {
			get => Data.Get("flying", EntityConstants.DefaultFlying);
			set => Data.Set("flying", value);
		}

		/// <inheritdoc />
		public bool IsCrouching {
			get => Data.Get("crouching", EntityConstants.DefaultCrouching);
			set => Data.Set("crouching", value);
		}

		/// <inheritdoc />
		public bool IsSprinting {
			get => Data.Get("sprinting", EntityConstants.DefaultSprinting);
			set => Data.Set("sprinting", value);
		}

		#endregion
	}
}
