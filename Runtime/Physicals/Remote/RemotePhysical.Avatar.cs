using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Nox.Avatars;
using Nox.Avatars.Parameters;
using Nox.Avatars.Rigging;
using Nox.CCK.Avatars;
using Nox.CCK.Players;
using Nox.CCK.Utils;
using UnityEngine;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Relay.Runtime.Physicals {
	/// <summary>
	/// Avatar loading and attaching for this physical: loading placeholder, announced avatar and
	/// error avatar. The announced identifier is resolved once the owner's properties are known.
	/// </summary>
	public partial class RemotePhysical {

		private Dictionary<string, object> AvatarParameters
			=> new() {
				["local"] = false,
				["desktop"] = true,
			};

		private IRuntimeAvatar RuntimeAvatar;
		private CancellationTokenSource AvatarLoadingCts;

		public void OnDestroy() {
			DisposeNameplate();
			CancelAvatarLoading();
			if (RuntimeAvatar == null) return;
			// Same as an avatar swap: Unbind() stops listening and drops the bindings, before the
			// avatar's playable graph dies.
			Reference?.Unbind();
			RuntimeAvatar.Dispose().Forget();
			RuntimeAvatar = null;
		}

		private void CancelAvatarLoading() {
			AvatarLoadingCts?.Cancel();
			AvatarLoadingCts?.Dispose();
			AvatarLoadingCts = null;
		}

		private async UniTask Setup() {
			_partStates.Clear();

			if (Reference?.Parts.TryGetValue(PlayerRig.Base.ToIndex(), out var part) == true) {
				transform.position        = part.Position;
				transform.rotation        = part.Rotation;
				transform.localScale      = part.Scale;
				rigidbody.linearVelocity  = part.Velocity;
				rigidbody.angularVelocity = part.Angular;
				_tickInterval = Reference.Reference.Room.Tps > 0 ? 1f / Reference.Reference.Room.Tps : 0.05f;
			}

			if (RuntimeAvatar != null) {
				Logger.LogDebug("Avatar already set for DesktopController");
				return;
			}

			if (Main.AvatarAPI == null) {
				Logger.LogWarning("AvatarAPI not available yet, skipping avatar setup");
				return;
			}

			if (Reference?.Avatar.IsValid() != true) {
				Logger.LogDebug("No usable announced avatar identifier, showing the error avatar.");
				await SetErrorAvatar();
				return;
			}

			Logger.LogDebug("Creating avatar");

			AvatarLoadingCts?.Cancel();
			AvatarLoadingCts = new CancellationTokenSource();

			var avatar = await Main.AvatarAPI.LoadLoading(AvatarParameters, token: AvatarLoadingCts.Token);
			if (avatar == null) {
				Logger.LogError("Failed to create avatar for DesktopController");
				return;
			}

			try {
				if (AvatarLoadingCts.IsCancellationRequested || !this || !gameObject)
					return;

				await SetAvatar(avatar);
				// Ownership transferred to SetAvatar (or disposed on failure)

				if (Reference?.Avatar.IsValid() == true) {
					AvatarLoadingCts?.Cancel();
					AvatarLoadingCts = new CancellationTokenSource();
					await SetAvatar(Reference.Avatar);
				}
			} finally {
				// If loading avatar wasn't attached (e.g. physical destroyed mid-setup), dispose it
				if (avatar != RuntimeAvatar)
					avatar.Dispose().Forget();
			}
		}

		public async UniTask<IRuntimeAvatar> SetAvatar(Identifier identifier) {
			if (Reference == null) {
				Logger.LogWarning("Reference is null, cannot set avatar.");
				return null;
			}

			Logger.LogDebug($"Loading avatar for identifier {identifier.ToString()}");

			if (Main.AvatarAPI == null) {
				Logger.LogWarning("AvatarAPI not available yet, cannot load avatar.");
				return null;
			}

			if (!identifier.IsValid()) {
				Logger.LogWarning($"Invalid avatar identifier: {identifier.ToString()}, showing the error avatar.");
				await SetErrorAvatar();
				return null;
			}

			if (identifier.Equals(RuntimeAvatar?.Identifier)) {
				Logger.LogDebug("Avatar identifier matches current avatar, no need to load.");
				return RuntimeAvatar;
			}

			AvatarLoadingCts?.Cancel();
			AvatarLoadingCts = new CancellationTokenSource();

			var req = new AssetSearchRequest {
				Engines   = new[] { EngineExtensions.CurrentEngine.GetEngineName() },
				Platforms = new[] { PlatformExtensions.CurrentPlatform.GetPlatformName() },
				Versions  = new[] { identifier.GetVersion() },
				Limit     = 1
			};

			var asset = (await Main.AvatarAPI.SearchAssets(identifier, req)
				.AttachExternalCancellation(AvatarLoadingCts.Token))
				.Items
				.FirstOrDefault();

			if (AvatarLoadingCts.IsCancellationRequested)
				return null;

			if (asset == null) {
				Logger.LogWarning($"Avatar asset not found for identifier {identifier.ToString()}");
				await SetErrorAvatar();
				return null;
			}

			if (!Main.AvatarAPI.HasInCache(asset.Hash)) {
				var download = Main.AvatarAPI.DownloadToCache(
					asset.Url,
					hash: asset.Hash,
					token: AvatarLoadingCts.Token
				);
				await download.Start();
				if (AvatarLoadingCts.IsCancellationRequested)
					return null;
			}

			var avatar = await Main.AvatarAPI.LoadFromCache(
				asset.Hash,
				AvatarParameters,
				token: AvatarLoadingCts.Token
			);

			if (AvatarLoadingCts.IsCancellationRequested)
				return null;

			if (avatar == null) {
				Logger.LogError($"Failed to load avatar from cache for identifier {identifier.ToString()}");
				await SetErrorAvatar();
				return null;
			}

			Logger.LogDebug($"Avatar loaded: {identifier.ToString()}");
			avatar.Identifier = identifier;
			await SetAvatar(avatar);
			return avatar;
		}

		/// <summary>
		/// Attaches the error avatar to this physical.
		/// </summary>
		/// <remarks>
		/// Used whenever an announced avatar cannot be resolved (invalid identifier, missing asset,
		/// load failure) so the remote player keeps a visible representation.
		/// <para>
		/// The error avatar's <see cref="IRuntimeAvatar.Identifier"/> is deliberately left at
		/// <see cref="Identifier.Invalid"/>: the announced identifier is not exploitable, and if we
		/// assigned it, the early-out <c>identifier.Equals(RuntimeAvatar?.Identifier)</c> in
		/// <see cref="SetAvatar(Identifier)"/> would consider the error avatar to be the real one
		/// and never retry the actual load.
		/// </para>
		/// </remarks>
		public async UniTask<IRuntimeAvatar> SetErrorAvatar() {
			if (Reference == null) {
				Logger.LogWarning("Reference is null, cannot set the error avatar.");
				return null;
			}

			if (Main.AvatarAPI == null) {
				Logger.LogWarning("AvatarAPI not available yet, cannot load the error avatar.");
				return null;
			}

			AvatarLoadingCts?.Cancel();
			AvatarLoadingCts = new CancellationTokenSource();

			var err = await Main.AvatarAPI.LoadError(AvatarParameters, token: AvatarLoadingCts.Token);
			if (AvatarLoadingCts.IsCancellationRequested || !this || !gameObject)
				return null;

			if (err == null) {
				Logger.LogError("Failed to load the error avatar.");
				return null;
			}

			return await SetAvatar(err) ? err : null;
		}

		public async UniTask<bool> SetAvatar(IRuntimeAvatar runtime) {
			if (runtime == RuntimeAvatar)
				return true;

			// If this physical is being destroyed, refuse to attach and clean up the incoming avatar
			if (!this || !gameObject || !transform) {
				Logger.LogWarning("Physical is being destroyed, disposing incoming avatar instead of attaching.");
				runtime?.Dispose().Forget();
				return false;
			}

			var old = RuntimeAvatar;
			RuntimeAvatar = runtime;
			_partStates.Clear();
			_rigProvider = null;

			// The plate reads a parameter of this avatar: its cache must not survive the swap.
			InvalidateHeight();

			if (RuntimeAvatar == null) {
				Logger.LogWarning("Setting avatar to null, removing current avatar.");
				RuntimeAvatar = old;
				return false;
			}

			var root = RuntimeAvatar.Descriptor.Anchor;
			if (!root) {
				Logger.LogError("Avatar descriptor root is null, cannot set avatar.");
				RuntimeAvatar = old;
				return false;
			}

			root.name += $" {runtime.Identifier.ToString()} {nameof(RemotePhysical)}";

			if (old != null) {
				Reference?.Unbind();
				await old.Dispose();
			}

			Logger.LogDebug($"Attaching avatar to {runtime.Descriptor}", runtime.Descriptor.Anchor);
			root.transform.SetParent(transform, false);
			root.transform.localPosition = Vector3.zero;
			root.transform.localRotation = Quaternion.identity;

			var module = RuntimeAvatar?.Descriptor
				?.GetModules<IParameterModule>()
				.FirstOrDefault();

			if (module == null) {
				Logger.LogWarning("Avatar has no parameter module, cannot configure tracking parameters.");
				return true;
			}

			// Attendre que l'Animator soit prêt avant de configurer les paramètres
			var animator = RuntimeAvatar?.Descriptor?.Animator;
			if (animator && !animator.runtimeAnimatorController) {
				Logger.LogDebug("Waiting for Animator to be ready...");
				await UniTask.WaitUntil(() => animator.runtimeAnimatorController);
			}

			_rigProvider = RuntimeAvatar?.Descriptor?.Anchor
				?.GetComponentInChildren<IRigProvider>(true);

			var parameters = module.GetParameters();
			foreach (var param in parameters)
				switch (param.Name) {
					case "rig/ik/head/target":
					case "tracking/left_hand/active":
					case "tracking/right_hand/active":
					case "tracking/left_foot/active":
					case "tracking/right_foot/active":
					case "tracking/right_toes/active":
					case "tracking/left_toes/active":
						param.Value = false;
						break;

					case "rig/ik/spine/position_weight":
					case "rig/ik/spine/hint_weight":
						param.Value = 0f;
						break;

					case "tracking/head/active":
					case "IsLocal":
						param.Value = true;
						break;
				}

			root.SetActive(true);
			Reference?.UpdateAvatar(runtime);

			return true;
		}

	}
}
