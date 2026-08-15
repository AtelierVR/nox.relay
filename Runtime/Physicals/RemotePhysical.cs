using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Nox.Avatars;
using Nox.Avatars.Parameters;
using Nox.Avatars.Rigging;
using Nox.CCK.Avatars;
using Nox.CCK.Events;
using Nox.CCK.Players;
using Nox.CCK.Utils;
using Nox.Relay.Runtime.Players;
using UnityEngine;
using Logger = Nox.CCK.Utils.Logger;
namespace Nox.Relay.Runtime.Physicals {
	public class RemotePhysical : Physical {
		/// <summary>Fired when a new avatar is fully set up on this physical. Listeners may migrate voice/camera sources.</summary>
		public readonly NoxEvent<IRuntimeAvatar> OnAvatarSet = new();

		public new RemotePlayer Reference {
			get => (RemotePlayer)base.Reference;
			set {
				base.Reference = value;
				Setup().Forget();
			}
		}

		private Dictionary<string, object> AvatarParameters
			=> new() {
				["local"] = false,
				["desktop"] = true,
			};

	private Rigidbody _rigidbody;
	private IRuntimeAvatar RuntimeAvatar;
	private CancellationTokenSource AvatarLoadingCts;

	// État d'interpolation par part (keyed by partId)
	private struct PartInterpolationState {
		public Vector3    StartPosition;
		public Vector3    TargetPosition;
		public Quaternion StartRotation;
		public Quaternion TargetRotation;
		public Vector3    StartScale;
		public Vector3    TargetScale;
		public float      PosTime;
		public float      RotTime;
		public float      ScaleTime;
	}

	private readonly Dictionary<ushort, PartInterpolationState> _partStates = new();
	private float _tickInterval;
	private IRiggingModule _riggingModule;

	private new Rigidbody rigidbody
		=> _rigidbody ??= gameObject.GetOrAddComponent<Rigidbody>();

		override protected void OnEnable() {
			base.OnEnable();
			Setup().Forget();
		}

		override protected void OnDisable() {
			// Cancel any in-progress avatar loading when this physical is hidden/destroying
			CancelAvatarLoading();
			base.OnDisable();
		}

		public void OnDestroy() {
			CancelAvatarLoading();
			if (RuntimeAvatar == null) return;
			RuntimeAvatar.Dispose().Forget();
			RuntimeAvatar = null;
		}

		private void CancelAvatarLoading() {
			AvatarLoadingCts?.Cancel();
			AvatarLoadingCts?.Dispose();
			AvatarLoadingCts = null;
		}

	private static float Smoothstep(float t) => t * t * (3f - 2f * t);

	private void Update() {
		if (Reference == null) return;

		var dt        = Time.deltaTime;
		var tps       = Reference.Reference.Room.Tps;
		var threshold = Reference.Reference.Room.Threshold;
		_tickInterval = tps > 0 ? 1f / tps : 0.05f;

		foreach (var (partId, part) in Reference.Parts) {
			var rig = partId.ToPlayerRig();

			var newTargetPos = part.Position;
			var newTargetRot = part.Rotation;
			var newTargetSca = part.Scale;

			if (!_partStates.TryGetValue(partId, out var state)) {
				// Première fois : snap immédiat sans interpolation
				state = new PartInterpolationState {
					StartPosition  = newTargetPos,
					TargetPosition = newTargetPos,
					StartRotation  = newTargetRot,
					TargetRotation = newTargetRot,
					StartScale     = newTargetSca,
					TargetScale    = newTargetSca,
					PosTime        = _tickInterval,
					RotTime        = _tickInterval,
					ScaleTime      = _tickInterval,
				};
			} else {
				if (Vector3.Distance(newTargetPos, state.TargetPosition) > threshold) {
					state.StartPosition  = rig == PlayerRig.Base ? transform.position : state.TargetPosition;
					state.TargetPosition = newTargetPos;
					state.PosTime        = 0f;
				}
				if (Quaternion.Angle(newTargetRot, state.TargetRotation) > threshold) {
					state.StartRotation  = rig == PlayerRig.Base ? transform.rotation : state.TargetRotation;
					state.TargetRotation = newTargetRot;
					state.RotTime        = 0f;
				}
				if (rig == PlayerRig.Base && Vector3.Distance(newTargetSca, state.TargetScale) > threshold) {
					state.StartScale  = transform.localScale;
					state.TargetScale = newTargetSca;
					state.ScaleTime   = 0f;
				}
			}

			state.PosTime   += dt;
			state.RotTime   += dt;
			state.ScaleTime += dt;

			var tPos   = Smoothstep(Mathf.Clamp01(state.PosTime   / _tickInterval));
			var tRot   = Smoothstep(Mathf.Clamp01(state.RotTime   / _tickInterval));
			var tScale = Smoothstep(Mathf.Clamp01(state.ScaleTime / _tickInterval));

			if (rig == PlayerRig.Base) {
				// Position
				if (Vector3.Distance(state.StartPosition, state.TargetPosition) > threshold * 0.1f) {
					transform.position = Vector3.Lerp(state.StartPosition, state.TargetPosition, tPos);
					if (dt > 0)
						rigidbody.linearVelocity = (state.TargetPosition - transform.position) / _tickInterval;
				} else {
					transform.position       = state.TargetPosition;
					rigidbody.linearVelocity = part.Velocity;
				}

				// Rotation
				if (Quaternion.Angle(state.StartRotation, state.TargetRotation) > threshold * 0.1f) {
					transform.rotation = Quaternion.Slerp(state.StartRotation, state.TargetRotation, tRot);
					var deltaRot = state.TargetRotation * Quaternion.Inverse(transform.rotation);
						deltaRot.ToAngleAxis(out var angle, out var axis);
						if (angle > 180f) angle -= 360f;
					if (_tickInterval > 0)
						rigidbody.angularVelocity = axis * (angle * Mathf.Deg2Rad / _tickInterval);
				} else {
					transform.rotation        = state.TargetRotation;
					rigidbody.angularVelocity = part.Angular;
					}

				// Scale
				transform.localScale = Vector3.Distance(state.StartScale, state.TargetScale) > threshold * 0.1f
					? Vector3.Lerp(state.StartScale, state.TargetScale, tScale)
					: state.TargetScale;
			} else if (_riggingModule != null && _riggingModule.TryGetPart(partId, out var rigPart)) {
				var rigTransform = rigPart.GetTransform();
				if (rigTransform != null) {
					var interpolatedPos = Vector3.Distance(state.StartPosition, state.TargetPosition) > threshold * 0.1f
						? Vector3.Lerp(state.StartPosition, state.TargetPosition, tPos)
						: state.TargetPosition;
					var interpolatedRot = Quaternion.Angle(state.StartRotation, state.TargetRotation) > threshold * 0.1f
						? Quaternion.Slerp(state.StartRotation, state.TargetRotation, tRot)
						: state.TargetRotation;
					rigTransform.SetPositionAndRotation(interpolatedPos, interpolatedRot);
				}			}

			_partStates[partId] = state;
		}
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
				Logger.LogWarning($"Invalid avatar identifier: {identifier.ToString()}");
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
				var err = await Main.AvatarAPI.LoadError(AvatarParameters);
				err.Identifier = identifier;
				await SetAvatar(err);
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
				var err = await Main.AvatarAPI.LoadError(AvatarParameters);
				err.Identifier = identifier;
				await SetAvatar(err);
				return null;
			}

			Logger.LogDebug($"Avatar loaded: {identifier.ToString()}");
			avatar.Identifier = identifier;
			await SetAvatar(avatar);
			return avatar;
		}

		public async UniTask<bool> SetAvatar(IRuntimeAvatar runtimeAvatar) {
			if (runtimeAvatar == RuntimeAvatar)
				return true;

			// If this physical is being destroyed, refuse to attach and clean up the incoming avatar
			if (!this || !gameObject || !transform) {
				Logger.LogWarning("Physical is being destroyed, disposing incoming avatar instead of attaching.");
				runtimeAvatar?.Dispose().Forget();
				return false;
			}

			var old = RuntimeAvatar;
			RuntimeAvatar = runtimeAvatar;
			_partStates.Clear();
			_riggingModule = null;

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

			root.name += $" {runtimeAvatar.Identifier.ToString()} {nameof(RemotePhysical)}";

			if (old != null)
				await old.Dispose();

			Logger.LogDebug($"Attaching avatar to {runtimeAvatar.Descriptor}", runtimeAvatar.Descriptor.Anchor);
			root.transform.SetParent(transform, false);
			root.transform.localPosition = Vector3.zero;
			root.transform.localRotation = Quaternion.identity;

			var parameterModule = RuntimeAvatar?.Descriptor
				?.GetModules<IParameterModule>()
				.FirstOrDefault();

			if (parameterModule == null) {
				Logger.LogWarning("Avatar has no parameter module, cannot configure tracking parameters.");
				return true;
			}

			// Attendre que l'Animator soit prêt avant de configurer les paramètres
			var animator = RuntimeAvatar?.Descriptor?.Animator;
			if (animator && !animator.runtimeAnimatorController) {
				Logger.LogDebug("Waiting for Animator to be ready...");
				await UniTask.WaitUntil(() => animator.runtimeAnimatorController);
			}

			_riggingModule = RuntimeAvatar?.Descriptor?.Anchor
				?.GetComponentInChildren<IRiggingModule>(true);

			var parameters = parameterModule.GetParameters();
			foreach (var param in parameters) {
				var n = param.GetName();
				switch (n) {
					case "rig/ik/head/target":
					case "tracking/left_hand/active":
					case "tracking/right_hand/active":
					case "tracking/left_foot/active":
					case "tracking/right_foot/active":
					case "tracking/right_toes/active":
					case "tracking/left_toes/active":
						param.Set(false);
						break;
					case "rig/ik/spine/position_weight":
					case "rig/ik/spine/hint_weight":
						param.Set(0f);
						break;
					case "tracking/head/active":
					case "IsLocal":
						param.Set(true);
						break;
				}
			}

			root.SetActive(true);

			OnAvatarSet.Invoke(runtimeAvatar);

			return true;
		}
	}
}