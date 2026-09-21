using System.Linq;
using Nox.Avatars.Controllers;
using Nox.Controllers;

namespace Nox.Relay.Runtime.Players {
	/// <summary>
	/// Controller binding for the local player: mirrors the active controller's parts into the
	/// player's part cache and forwards the controller's avatar to <see cref="UpdateAvatar"/>.
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
		}

		internal void RemoveController() {
			foreach (var part in Parts.Values)
				if (part is Part p)
					p.Store();
		}

	}
}
