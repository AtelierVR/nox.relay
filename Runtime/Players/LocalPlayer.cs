using System.Collections.Generic;
using System.Linq;
using Nox.Controllers;
using CorePlayer = Nox.Relay.Core.Players.Player;

namespace Nox.Relay.Runtime.Players {
	public class LocalPlayer : Player {
		public LocalPlayer(Entities context, CorePlayer player) : base(context, player) { }

		public override bool IsLocal => true;

		internal void UpdateController(IController controller) {
			if (controller == null) {
				RemoveController();
				return;
			}

			var cParts = controller.GetParts();

			// remove parts not in controller
			var keysToRemove = _parts.Keys.Except(cParts.Select(p => p.Key)).ToList();
			foreach (var key in keysToRemove)
				_parts.Remove(key);

			// add parts from controller
			foreach (var cPart in cParts) {
				if (_parts.ContainsKey(cPart.Key)) continue;
				_parts[cPart.Key] = new Part(this, cPart.Key);
			}

			// restore parts
			foreach (var part in _parts.Values) {
				if (part is Part p)
					p.Restore(controller);
			}
		}

		internal void RemoveController() {
			foreach (var part in _parts.Values) {
				if (part is Part p)
					p.Store();
			}
		}
	}
}