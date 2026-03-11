using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Nox.Avatars.Controllers;
using Nox.Avatars.Parameters;
using Nox.CCK.Utils;
using Nox.Controllers;
using Nox.Relay.Core.Types.Avatars;
using CorePlayer = Nox.Relay.Core.Players.Player;

namespace Nox.Relay.Runtime.Players {
	public class LocalPlayer : Player {
		public LocalPlayer(Entities context, CorePlayer player) : base(context, player) { }

		public override bool IsLocal
			=> true;

		public override void Update() {
			// Disabled for local player - no interpolation needed
		}

		// Note: Tick() is inherited from Player.cs and handles SendTransformsIfNeeded()

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
				if (Parts.ContainsKey(cPart.Key))
					continue;
				Parts[cPart.Key] = new Part(this, cPart.Key);
			}

			// Initialize each part's cache with current controller values
			foreach (var part in Parts.Values)
				if (part is Part p)
					p.Restore(controller);

			// Synchronize avatar parameters as properties
			if (controller is IControllerAvatar avatarController)
				UpdateAvatarOfController(avatarController);
		}

		internal void RemoveController() {
			foreach (var part in Parts.Values)
				if (part is Part p)
					p.Store();
		}

		/// <summary>
		/// Synchronizes avatar parameters as properties on the entity.
		/// Uses the base class SynchronizeAvatarParameters() method.
		/// </summary>
		internal void UpdateAvatarOfController(IControllerAvatar controller)
			=> UpdateAvatarOfControllerAsync(controller).Forget();

		private async UniTask UpdateAvatarOfControllerAsync(IControllerAvatar controller) {
			var avatar   = controller.GetAvatar();
			var response = await Context.Context.Room.ChangeAvatar(AvatarChangedRequest.Self(avatar.GetIdentifier()));
			if (response.IsError) {
				Logger.LogWarning($"Failed to change avatar: {response.Reason}");
				return;
			}

			var descriptor = avatar?.GetDescriptor();
			var parameterModule = descriptor
				?.GetModules<IParameterModule>()
				.FirstOrDefault();

			var parameters = parameterModule
					?.GetParameters()
				?? Array.Empty<IParameter>();

			SynchronizeAvatarParameters(parameters, isLocal: true);
		}
	}
}