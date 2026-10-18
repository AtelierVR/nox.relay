using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Nox.Avatars;
using Nox.CCK.Sessions;
using Nox.CCK.Utils;
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
		/// <summary>Loaded dimensions of the session (<c>null</c> until the world is loaded).</summary>
		internal Dimensions _dimensions;

		/// <summary>
		/// World identifier requested by the current travel, kept until the destination scene
		/// (and therefore the dimensions) is available. <see cref="Identifier.Invalid"/> when there
		/// is no pending travel (a travel by URL has no world identity).
		/// </summary>
		private Identifier _identifier;

		/// <summary>
		/// Prepare the dimension state of this session (called once from the constructor): the world
		/// itself is loaded later, by <see cref="SetDimension"/>.
		/// </summary>
		private void SetupDimensions() {
			_dimensions   = null;
			_identifier = Identifier.Invalid;
		}

		public IDimensions Dimensions
			=> _dimensions;

		/// <summary>
		/// Whether this session targets the given world. While the destination world is still
		/// loading there is no dimension yet, so the requested identifier is used instead: that
		/// is what lets the UI show the progress (and offer to cancel) of a pending session.
		/// </summary>
		public bool Match(Identifier identifier) {
			var current = _dimensions != null ? _dimensions.Identifier : _identifier;
			return current.IsValid() && current.Equals(identifier);
		}

		private void SetDimension(IRuntimeWorld scene) {
			_dimensions?.Dispose();
			_dimensions = new Dimensions(this, scene);
			_identifier = scene.Identifier;
		}

		/// <summary>
		/// Every session module of the loaded dimensions, flattened.
		/// <para>
		/// Flat loop instead of
		/// <c>GetDescriptors().Where(e =&gt; e != null).SelectMany(d =&gt; d.GetModules&lt;ISessionModule&gt;()).ToArray()</c>:
		/// that chain allocated a closure and two iterators per call, and the generic
		/// <c>GetModules&lt;T&gt;()</c> copied the descriptor's array a second time through
		/// <c>OfType&lt;T&gt;().ToArray()</c>. The non-generic <c>GetModules()</c> hands out the stored array,
		/// so the type test happens here, in one pass.
		/// </para>
		/// </summary>
		private ISessionModule[] GetModules() {
			var dimensions = _dimensions;
			if (dimensions == null)
				return Array.Empty<ISessionModule>();

			var modules     = new List<ISessionModule>();
			var descriptors = dimensions.GetDescriptors();

			for (var i = 0; i < descriptors.Length; i++) {
				var descriptor = descriptors[i];
				if (descriptor == null)
					continue;

				var descriptorModules = descriptor.GetModules();
				for (var j = 0; j < descriptorModules.Length; j++)
					if (descriptorModules[j] is ISessionModule module)
						modules.Add(module);
			}

			return modules.ToArray();
		}

		public async UniTask OnSelect(ISession old) {
			Logger.LogDebug("Selecting session", tag: Tag);

			if (_dimensions == null) {
				Logger.LogDebug("No dimension found", tag: Tag);
				return;
			}

			if (!await _dimensions.CreateIfMissing(Runtime.Dimensions.MainIndex)) {
				Logger.LogError("Failed to create main scene", tag: Tag);
				return;
			}

			_dimensions.SetActive(Runtime.Dimensions.MainIndex, true);
			_dimensions.SetCurrent();
			Current = this;

			if (_entities.LocalPlayer is Player { NeedsRespawn: true } localPlayer)
				localPlayer.Respawn();

			OnControllerChanged(Main.ControllerAPI.Current);

			foreach (var module in GetModules())
				module.OnSessionSelected();
		}

		public void OnSceneLoaded(int index, IWorldDescriptor descriptor, GameObject anchor) {
			Main.CoreAPI.EventAPI.Emit("session_scene_added", this, index, descriptor, anchor);

			if (_entities.LocalPlayer is Player { NeedsRespawn: true } localPlayer)
				localPlayer.Respawn();

			var modules = descriptor.GetModules<ISessionModule>();
			Logger.LogDebug($"OnDescriptorAdded: {descriptor} with {modules.Length} modules", descriptor as Object, Tag);

			foreach (var module in modules)
				module.OnLoaded(this);

			if (IsConnected)
				foreach (var module in modules)
					TryNotifyConnected(module);

			foreach (var player in _entities.GetEntities<IPlayer>()) {
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

			foreach (var module in GetModules())
				module.OnSceneLoaded(descriptor, index, anchor);
		}

		public void OnSceneUnloaded(int index) {
			Main.CoreAPI.EventAPI.Emit("session_scene_removed", this, index);

			foreach (var module in GetModules())
				module.OnSceneUnloaded(index);
		}

		public void OnControllerChanged(IController controller)
			=> _entities.LocalPlayer?.UpdateController(controller);

		public void OnAvatarOfControllerChanged(IRuntimeAvatar avatar)
			=> _entities.LocalPlayer?.UpdateAvatar(avatar);

		public async UniTask OnDeselect(ISession @new) {
			Logger.LogDebug("Deselecting session", tag: Tag);

			if (_dimensions == null) {
				Logger.LogWarning("The scene has no dimension assigned. Skipping visibility updates.", tag: Tag);
				await UniTask.Yield();
				return;
			}

			foreach (var module in GetModules())
				module.OnSessionDeselected();

			_dimensions.SetActive(Runtime.Dimensions.MainIndex, false);

			foreach (var entity in _entities.GetEntities<Entity>())
				entity.DestroyPhysical(true);

			await UniTask.Yield();

			_entities.LocalPlayer?.RemoveController();
		}
	}
}
