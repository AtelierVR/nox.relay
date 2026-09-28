using System.Linq;
using Nox.Avatars.Controllers;
using Nox.CCK.Nameplate;
using Nox.CCK.Sessions;
using Nox.Controllers;

namespace Nox.Relay.Runtime.Players {
	/// <summary>
	/// Controller binding for the local player: mirrors the active controller's parts into the
	/// player's part cache, forwards the controller's avatar to <see cref="UpdateAvatar"/>, and pushes
	/// the player state (abilities, nameplate) back onto the controller.
	/// </summary>
	public partial class LocalPlayer {

		internal void UpdateController(IController controller) {
			if (controller == null) {
				RemoveController();
				return;
			}

			var cp = controller.GetParts();

			// Remove parts that no longer exist in the controller
			var rm = Parts.Keys.Except(cp.Select(p => p.Key)).ToList();
			foreach (var key in rm)
				Parts.Remove(key);

			// Add new parts from the controller (initialize cache)
			foreach (var p in cp) {
				if (Parts.ContainsKey(p.Key))
					continue;
				Parts[p.Key] = new Part(this, p.Key);
			}

			// Initialize each part's cache with current controller values
			foreach (var part in Parts.Values)
				if (part is Part p)
					p.Restore(controller);

			// Synchronize avatar parameters as properties
			if (controller is IControllerAvatar ac)
				UpdateAvatar(ac.GetAvatar());

			ApplyToController(controller);
		}

		internal void RemoveController() {
			foreach (var part in Parts.Values)
				if (part is Part p)
					p.Store();
		}

		/// <summary>
		/// Pushes the local player state onto the controller: the movement abilities
		/// (<see cref="AbilitiesConstants.MaxMoveSpeed"/>, <see cref="AbilitiesConstants.JumpForce"/>, ...),
		/// the team colour and the health bar of the plate.
		/// <para>
		/// The plate <b>visibility</b> is deliberately not pushed here: the plate of the local client is
		/// driven by the controller itself (its menu provider pushes <c>Keys.VISIBLE</c> on it), and the
		/// plates of the remote players by the scripts.
		/// </para>
		/// Called when the controller is bound — which is exactly when the session becomes current
		/// (<c>ISessionAPI.SetCurrent</c> → <c>Session.OnSelect</c> or player entered) or when the
		/// controller itself changes — and again on every data change while the session is current.
		/// </para>
		/// </summary>
		internal void ApplyToController(IController controller) {
			if (controller == null)
				return;

			controller.SetAbilities(AbilitiesConstants.MaxMoveSpeed, WalkSpeed);
			controller.SetAbilities(AbilitiesConstants.MoveAcceleration, MoveAcceleration);
			controller.SetAbilities(AbilitiesConstants.JumpForce, JumpForce);
			controller.SetAbilities(AbilitiesConstants.SprintMultiplier, SprintMultiplier);
			controller.SetAbilities(AbilitiesConstants.AirControl, AirControl);
			controller.SetAbilities(AbilitiesConstants.FlySpeed, FlySpeed);
			controller.SetAbilities(AbilitiesConstants.MayFly, MayFly);
			controller.SetAbilities(AbilitiesConstants.Immobilized, IsImmobilized);
			controller.SetAbilities(AbilitiesConstants.Flying, IsFlying);
			controller.SetAbilities(AbilitiesConstants.Crouching, IsCrouching);
			controller.SetAbilities(AbilitiesConstants.Sprinting, IsSprinting);

			if (controller is INameplateHolder holder && holder.Nameplate.IsAlive()) {
				PushHealthbar(holder.Nameplate);
				PushTeam(holder.Nameplate);
			}
		}

		/// <summary>
		/// Re-applies the player state to the controller when the entity data changes, but only while
		/// this session is the current one.
		/// </summary>
		private void OnEntityDataChanged(string[] key, object @new, object @old) {
			if (!SessionHelper.IsCurrent(Main.SessionAPI, Context.Context))
				return;

			ApplyToController(Main.ControllerAPI?.Current);
		}
	}
}
