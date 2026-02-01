using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Nox.Avatars;
using Nox.Avatars.Parameters;
using Nox.Avatars.Runtime.Network;
using Nox.CCK.Players;
using Nox.CCK.Utils;
using Nox.Relay.Runtime.Players;
using UnityEngine;
using Logger = Nox.CCK.Utils.Logger;
namespace Nox.Relay.Runtime.Physicals {
	public class RemotePhysical : Physical {
		public new RemotePlayer Reference {
			get => (RemotePlayer)base.Reference;
			set {
				base.Reference = value;
				Setup().Forget();
			}
		}

		private Dictionary<string, object> AvatarParameters
			=> new Dictionary<string, object> {
				["local"] = false,
				["desktop"] = true,
			};

	private Rigidbody _rigidbody;
	private IRuntimeAvatar RuntimeAvatar;
	private CancellationTokenSource AvatarLoadingCts;
	
	// Variables pour l'interpolation fluide
	private Vector3 _startPosition;
	private Vector3 _targetPosition;
	private Quaternion _startRotation;
	private Quaternion _targetRotation;
	private Vector3 _startScale;
	private Vector3 _targetScale;
	private float _interpolationTime;
	private float _tickInterval;

	private new Rigidbody rigidbody
		=> _rigidbody ??= gameObject.GetOrAddComponent<Rigidbody>();

		override protected void OnEnable() {
			base.OnEnable();
			Setup().Forget();
		}

	private void Update() {
		var part = Reference.Parts.GetValueOrDefault(PlayerRig.Base.ToIndex());
		if (part == null) return;

		var dt = Time.deltaTime;
		var tps = Reference.Reference.Room.Tps;
		var threshold = Reference.Reference.Room.Threshold;

		// Calculer l'intervalle entre deux ticks du serveur
		_tickInterval = tps > 0 ? 1f / tps : 0.05f;

		// Vérifier si nous avons reçu de nouvelles données du serveur
		var newTargetPosition = part.Position;
		var newTargetRotation = part.Rotation;
		var newTargetScale = part.Scale;

		// Si la cible a changé significativement, réinitialiser l'interpolation
		if (Vector3.Distance(newTargetPosition, _targetPosition) > threshold) {
			_startPosition = transform.position;
			_targetPosition = newTargetPosition;
			_interpolationTime = 0f;
		}

		if (Quaternion.Angle(newTargetRotation, _targetRotation) > threshold) {
			_startRotation = transform.rotation;
			_targetRotation = newTargetRotation;
			_interpolationTime = 0f;
		}

		if (Vector3.Distance(newTargetScale, _targetScale) > threshold) {
			_startScale = transform.localScale;
			_targetScale = newTargetScale;
		}

		// Incrémenter le temps d'interpolation
		_interpolationTime += dt;

		// Calculer le facteur d'interpolation (de 0 à 1 sur la durée d'un tick)
		var t = Mathf.Clamp01(_interpolationTime / _tickInterval);

		// Utiliser une courbe de lissage (smoothstep) pour éviter les à-coups
		t = t * t * (3f - 2f * t);

		// Interpoler la position
		if (Vector3.Distance(_startPosition, _targetPosition) > threshold * 0.1f) {
			transform.position = Vector3.Lerp(_startPosition, _targetPosition, t);
			
			// Mettre à jour la vélocité pour la physique
			if (dt > 0)
				rigidbody.linearVelocity = (_targetPosition - transform.position) / _tickInterval;
		} else {
			transform.position = _targetPosition;
			rigidbody.linearVelocity = part.Velocity;
		}

		// Interpoler la rotation
		if (Quaternion.Angle(_startRotation, _targetRotation) > threshold * 0.1f) {
			transform.rotation = Quaternion.Slerp(_startRotation, _targetRotation, t);
			
			// Mettre à jour la vélocité angulaire
			var deltaRotation = _targetRotation * Quaternion.Inverse(transform.rotation);
			deltaRotation.ToAngleAxis(out var angle, out var axis);
			if (angle > 180f) angle -= 360f;
			if (_tickInterval > 0)
				rigidbody.angularVelocity = axis * (angle * Mathf.Deg2Rad / _tickInterval);
		} else {
			transform.rotation = _targetRotation;
			rigidbody.angularVelocity = part.Angular;
		}

		// Interpoler l'échelle
		if (Vector3.Distance(_startScale, _targetScale) > threshold * 0.1f) {
			transform.localScale = Vector3.Lerp(_startScale, _targetScale, t);
		} else {
			transform.localScale = _targetScale;
		}
	}

	private async UniTask Setup() {
		if (Reference?.Parts.TryGetValue(PlayerRig.Base.ToIndex(), out var part) == true) {
			transform.position = part.Position;
			transform.rotation = part.Rotation;
			transform.localScale = part.Scale;
			rigidbody.linearVelocity = part.Velocity;
			rigidbody.angularVelocity = part.Angular;
			
			// Initialiser les variables d'interpolation
			_startPosition = part.Position;
			_targetPosition = part.Position;
			_startRotation = part.Rotation;
			_targetRotation = part.Rotation;
			_startScale = part.Scale;
			_targetScale = part.Scale;
			_interpolationTime = 0f;
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

			AvatarLoadingCts?.Cancel();
			AvatarLoadingCts = new CancellationTokenSource();

			await SetAvatar(avatar);

			if (Reference != null && Reference.Avatar.IsValid())
				await SetAvatar(Reference.Avatar);
		}

		public async UniTask<IRuntimeAvatar> SetAvatar(IAvatarIdentifier identifier) {
			if (Reference == null) {
				Logger.LogWarning("Reference is null, cannot set avatar.");
				return null;
			}

			Logger.LogDebug($"Loading avatar for identifier {identifier?.ToString() ?? "null"}");

			if (Main.AvatarAPI == null) {
				Logger.LogWarning("AvatarAPI not available yet, cannot load avatar.");
				return null;
			}

			if (identifier == null || !identifier.IsValid()) {
				Logger.LogWarning($"Invalid avatar identifier: {identifier?.ToString() ?? "null"}");
				return null;
			}

			if (identifier.Equals(RuntimeAvatar?.GetIdentifier())) {
				Logger.LogDebug("Avatar identifier matches current avatar, no need to load.");
				return RuntimeAvatar;
			}

			AvatarLoadingCts?.Cancel();
			AvatarLoadingCts = new CancellationTokenSource();

			var req = new AssetSearchRequest {
				Engines = new[] { EngineExtensions.CurrentEngine.GetEngineName() },
				Platforms = new[] { PlatformExtensions.CurrentPlatform.GetPlatformName() },
				Versions = new[] { identifier.GetVersion() },
				Limit = 1
			};

			var asset = (await Main.AvatarAPI.SearchAssets(identifier, req)
					.AttachExternalCancellation(AvatarLoadingCts.Token))
				.GetAssets()
				.FirstOrDefault();

			if (AvatarLoadingCts.IsCancellationRequested)
				return null;

			if (asset == null) {
				Logger.LogWarning($"Avatar asset not found for identifier {identifier.ToString()}");
				var err = await Main.AvatarAPI.LoadError(AvatarParameters);
				err.SetIdentifier(identifier);
				await SetAvatar(err);
				return null;
			}

			if (!Main.AvatarAPI.HasInCache(asset.GetHash())) {
				var download = Main.AvatarAPI.DownloadToCache(
					asset.GetUrl(),
					hash: asset.GetHash(),
					token: AvatarLoadingCts.Token
				);
				await download.Start();
				if (AvatarLoadingCts.IsCancellationRequested)
					return null;
			}

			var avatar = await Main.AvatarAPI.LoadFromCache(
				asset.GetHash(),
				AvatarParameters,
				token: AvatarLoadingCts.Token
			);

			if (AvatarLoadingCts.IsCancellationRequested)
				return null;

			if (avatar == null) {
				Logger.LogError($"Failed to load avatar from cache for identifier {identifier.ToString()}");
				var err = await Main.AvatarAPI.LoadError(AvatarParameters);
				err.SetIdentifier(identifier);
				await SetAvatar(err);
				return null;
			}

			Logger.LogDebug($"Avatar loaded: {identifier.ToString()}");
			avatar.SetIdentifier(identifier);
			await SetAvatar(avatar);
			return avatar;
		}

		public async UniTask<bool> SetAvatar(IRuntimeAvatar runtimeAvatar) {
			if (runtimeAvatar == RuntimeAvatar)
				return true;

			var old = RuntimeAvatar;
			RuntimeAvatar = runtimeAvatar;

			if (RuntimeAvatar == null) {
				Logger.LogWarning("Setting avatar to null, removing current avatar.");
				RuntimeAvatar = old;
				return false;
			}

			var root = RuntimeAvatar.GetDescriptor().GetAnchor();
			if (!root) {
				Logger.LogError("Avatar descriptor root is null, cannot set avatar.");
				RuntimeAvatar = old;
				return false;
			}

			root.name += $" {runtimeAvatar.GetIdentifier()?.ToString() ?? "null"} {nameof(RemotePhysical)}";

			if (old != null)
				await old.Dispose();

			Logger.LogDebug($"Attaching avatar to {runtimeAvatar.GetDescriptor()}", runtimeAvatar.GetDescriptor().GetAnchor());
			root.transform.SetParent(transform, false);
			root.transform.localPosition = Vector3.zero;
			root.transform.localRotation = Quaternion.identity;

			var parameterModule = RuntimeAvatar?.GetDescriptor()
				?.GetModules<IParameterModule>()
				.FirstOrDefault();

			if (parameterModule == null) {
				Logger.LogWarning("Avatar has no parameter module, cannot configure tracking parameters.");
				return true;
			}

			// Attendre que l'Animator soit prêt avant de configurer les paramètres
			var animator = RuntimeAvatar?.GetDescriptor()?.GetAnimator();
			if (animator && !animator.runtimeAnimatorController) {
				Logger.LogDebug("Waiting for Animator to be ready...");
				await UniTask.WaitUntil(() => animator.runtimeAnimatorController);
			}

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

			return true;
		}
	}
}