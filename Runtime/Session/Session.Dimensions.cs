using Cysharp.Threading.Tasks;
using Nox.Avatars;
using Nox.CCK.Sessions;
using Nox.Controllers;
using Nox.Players;
using Nox.Sessions;
using Nox.Worlds;
using UnityEngine;
using Logger = Nox.CCK.Utils.Logger;
using Object = UnityEngine.Object;
using Player = Nox.Relay.Runtime.Players.Player;

namespace Nox.Relay.Runtime {
	/// <summary>World/dimension selection lifecycle and controller/avatar binding.</summary>
	public sealed partial class Session {
		public async UniTask OnSelect(ISession old) {
			Logger.LogDebug("Selecting session", tag: Tag);

			if (InterDimensions == null) {
				Logger.LogDebug("No dimension found", tag: Tag);
				return;
			}

			if (!await InterDimensions.CreateIfMissing(Runtime.Dimensions.MainIndex)) {
				Logger.LogError("Failed to create main scene", tag: Tag);
				return;
			}

			InterDimensions.SetActive(Runtime.Dimensions.MainIndex, true);
			InterDimensions.SetCurrent();
			Current = this;

			if (InterEntities.LocalPlayer is Player { NeedsRespawn: true } localPlayer)
				localPlayer.Respawn();

			OnControllerChanged(Main.ControllerAPI.Current);

			foreach (var module in GetAllModules())
				module.OnSessionSelected();
		}

		public void OnSceneLoaded(int index, IWorldDescriptor descriptor, GameObject anchor) {
			Main.CoreAPI.EventAPI.Emit("session_scene_added", this, index, descriptor, anchor);

			if (InterEntities.LocalPlayer is Player { NeedsRespawn: true } localPlayer)
				localPlayer.Respawn();

			var modules = descriptor.GetModules<ISessionModule>();
			Logger.LogDebug($"OnDescriptorAdded: {descriptor} with {modules.Length} modules", descriptor as Object, Tag);

			foreach (var module in modules)
				module.OnLoaded(this);

			foreach (var player in InterEntities.GetEntities<IPlayer>()) {
				if (player == null)
					continue;
				foreach (var module in modules)
					module.OnPlayerJoined(player);
			}

			var master = MasterPlayer;
			if (master != null)
				foreach (var module in modules)
					module.OnAuthorityTransferred(master, null);

			if (SessionHelper.IsCurrent(Main.SessionAPI, this))
				foreach (var module in modules)
					module.OnSessionSelected();

			foreach (var module in GetAllModules())
				module.OnSceneLoaded(descriptor, index, anchor);
		}

		public void OnSceneUnloaded(int index) {
			Main.CoreAPI.EventAPI.Emit("session_scene_removed", this, index);

			foreach (var module in GetAllModules())
				module.OnSceneUnloaded(index);
		}

		public void OnControllerChanged(IController controller)
			=> InterEntities.LocalPlayer?.UpdateController(controller);

		public void OnAvatarOfControllerChanged(IRuntimeAvatar avatar)
			=> InterEntities.LocalPlayer?.UpdateAvatar(avatar);

		public async UniTask OnDeselect(ISession @new) {
			Logger.LogDebug("Deselecting session", tag: Tag);

			if (InterDimensions == null) {
				Logger.LogWarning("The scene has no dimension assigned. Skipping visibility updates.", tag: Tag);
				await UniTask.Yield();
				return;
			}

			foreach (var module in GetAllModules())
				module.OnSessionDeselected();

			InterDimensions.SetActive(Runtime.Dimensions.MainIndex, false);

			foreach (var entity in InterEntities.GetEntities<Entity>())
				entity.DestroyPhysical(true);

			await UniTask.Yield();

			InterEntities.LocalPlayer?.RemoveController();
		}
	}
}
