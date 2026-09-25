using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using Nox.Avatars.Parameters;
using Nox.CCK;
using Nox.CCK.Players;
using Nox.CCK.Utils;
using Nox.CCK.Nameplate;
using Nox.Entities;
using Nox.Nameplate;
using UnityEngine;
using Logger = Nox.CCK.Utils.Logger;
using Keys = Nox.CCK.Nameplate.Constants;

namespace Nox.Relay.Runtime.Physicals {
	/// <summary>
	/// Nameplate binding for a remote player physical.
	/// <para>
	/// The physical owns a <b>dedicated anchor</b> (<see cref="NameplateAnchor"/>) placed just
	/// above the player's head, and asks <c>nox.nameplate</c> for an <see cref="INameplate"/>
	/// through that anchor. It then feeds the plate: the user profile (fetched from
	/// <c>IUserAPI</c>) and the heartbar (from the player data <c>heart</c>/<c>heart.max</c>).
	/// </para>
	/// <para>
	/// The anchor is a child of the physical, so hiding the physical (relay hides it before
	/// releasing it) automatically hides the plate.
	/// </para>
	/// </summary>
	public partial class RemotePhysical {
		private const float NameplateOffset = 0.35f;

		/// <summary>Fallback head height when neither the avatar bone nor the head part is available.</summary>
		private const float NameplateFallbackHeight = 1.8f;

		/// <summary>Dedicated transform handed to the nameplate mod (above the player's head).</summary>
		public Transform NameplateAnchor { get; private set; }

		private INameplate _nameplate;
		private Transform   _headBone;
		private bool        _creatingNameplate;

		// Avatar Height parameter, cached until invalidated: Physical.OnParameterChanged (raised by the
		// player once the avatar module registered its parameters) and any avatar change reset it.
		private IParameter _heightParameter;

		/// <summary>Called every frame from the physical update.</summary>
		private void UpdateNameplate() {
			var anchor = EnsureNameplateAnchor();
			if (anchor == null)
				return;

			UpdateAnchorPosition(anchor);
			EnsureNameplate(anchor);
		}

		private Transform EnsureNameplateAnchor() {
			if (NameplateAnchor != null)
				return NameplateAnchor;

			var go = new GameObject("Nameplate Anchor");
			go.transform.SetParent(transform, false);

			NameplateAnchor = go.transform;
			return NameplateAnchor;
		}

		/// <summary>
		/// Places the anchor above the top of the head from the synced avatar <c>Height</c> parameter,
		/// mirroring the desktop proxy, so the plate follows the avatar size (and its crouch) instead of
		/// the head bone. Falls back to the head bone / networked head part while the parameter is not
		/// available yet (avatar loading, avatar without the scale module).
		/// </summary>
		private void UpdateAnchorPosition(Transform anchor) {
			var height = ResolveHeight();
			if (height > 0f) {
				anchor.position = transform.position + Vector3.up * (height + NameplateOffset);
				return;
			}

			var head = ResolveHeadBone();
			if (head != null) {
				anchor.position = head.position + Vector3.up * NameplateOffset;
				return;
			}

			// Networked head part (what the avatar rig is driven with).
			if (Reference != null
				&& Reference.TryGetPart(PlayerRig.Head.ToIndex(), out IPart part)
				&& part != null
				&& part.Updated != default) {
				anchor.position = part.Position + Vector3.up * NameplateOffset;
				return;
			}

			if (Reference != null)
				anchor.position = Reference.Position + Vector3.up * NameplateFallbackHeight;
		}

		/// <summary>Avatar height in meters, from the synced <c>Height</c> parameter (0 when unavailable).</summary>
		private float ResolveHeight() {
			var parameter = ResolveHeightParameter();
			return parameter != null ? parameter.Value.ToFloat() : 0f;
		}

		private IParameter ResolveHeightParameter() {
			if (_heightParameter != null)
				return _heightParameter;

			var module = RuntimeAvatar?.Descriptor?
				.GetModules<IParameterModule>()
				.FirstOrDefault();

			// "EyeHeight" is the legacy name kept for avatars authored before the rename.
			_heightParameter = module?.GetParameter("Height") 
				?? module?.GetParameter("EyeHeight");
			return _heightParameter;
		}

		/// <summary>
		/// Raised through <see cref="Physical.OnParameterChanged"/> when the bound player's avatar
		/// module registers or unregisters a parameter. The avatar and its parameters are attached
		/// asynchronously, so the plate reacts here instead of polling for <c>Height</c> to appear.
		/// </summary>
		private void OnNameplateParameterChanged(IParameter parameter) {
			var name = parameter?.Name;
			if (name != "Height" && name != "EyeHeight")
				return;

			InvalidateHeight();
			RefreshNameplate();
		}

		private void InvalidateHeight()
			=> _heightParameter = null;

		/// <summary>Immediate update from an event, outside of the per-frame pass.</summary>
		private void RefreshNameplate() {
			if (!this || !gameObject)
				return;

			var anchor = EnsureNameplateAnchor();
			if (anchor == null)
				return;

			UpdateAnchorPosition(anchor);
		}

		private Transform ResolveHeadBone() {
			// The avatar may be swapped at any time: a destroyed bone falls back to null
			// and is resolved again on the next frame.
			if (_headBone != null)
				return _headBone;

			var animator = RuntimeAvatar?.Descriptor?.Animator;
			if (animator == null)
				return null;

			_headBone = animator.GetBoneTransform(HumanBodyBones.Head);
			return _headBone;
		}

		private void EnsureNameplate(Transform anchor) {
			if (_nameplate.IsAlive() || _creatingNameplate)
				return;

			var api = Main.NameplateAPI;
			if (api == null)
				return;

			CreateNameplateAsync(api, anchor).Forget();
		}

		private async UniTaskVoid CreateNameplateAsync(INameplateAPI api, Transform anchor) {
			_creatingNameplate = true;
			try {
				var plate = await api.Instantiate(anchor);
				if (!plate.IsAlive())
					return;

				// The physical (and its anchor) may have been destroyed while instantiating.
				if (!this || !gameObject) {
					plate.Dispose();
					return;
				}

				_nameplate = plate;
				Reference?.Data?.OnChanged.AddListener(OnPlayerDataChanged);

				PushBadges();
				PushHeart();
				PushDisplay();
				FetchNameplateUser().Forget();
			} finally {
				_creatingNameplate = false;
			}
		}

		/// <summary>Fetch the user profile of this player and push it to the plate.</summary>
		private async UniTaskVoid FetchNameplateUser() {
			var api    = Main.UserAPI;
			var player = Reference;
			if (api == null || player == null)
				return;

			try {
				var user = await api.Fetch(player.Identifier);
				if (user != null && _nameplate.IsAlive())
					_nameplate.Set(Keys.USER, user);
			} catch (Exception e) {
				Logger.LogDebug($"Failed to fetch user '{player.Identifier.ToString()}' for the nameplate: {e.Message}", tag: nameof(RemotePhysical));
			}
		}

		private void OnPlayerDataChanged(string[] key, object @new, object old)
			=> PushHeart();

		/// <summary>
		/// Pushes the player's announced platform and engine as the plate badges
		/// (<c>icons/windows.png</c>, <c>icons/unity.png</c>, ...).
		/// </summary>
		private void PushBadges() {
			var player = Reference;
			if (player == null)
				return;

			_nameplate.SetClientBadges(player.Platform, player.Engine);
		}

		/// <summary>
		/// Pushes the display name relay computed for this player (its <c>Display</c> is customisable,
		/// e.g. a renamed player), overriding the profile's own name.
		/// </summary>
		private void PushDisplay() {
			var player = Reference;
			if (player == null)
				return;

			_nameplate.Set(Keys.DISPLAY, player.Display);
		}

		private void PushHeart() {
			var plate = _nameplate;
			if (!plate.IsAlive())
				return;

			var data = Reference?.Data;
			if (data == null || !data.Has("heart")) {
				plate.Set(Keys.HEARTS_VISIBLE, false);
				return;
			}

			plate.Set(Keys.HEARTS_MAX, data.Get("heart.max", 100f));
			plate.Set(Keys.HEARTS_VALUE, data.Get("heart", 0f));
		}

		private void DisposeNameplate() {
			if (_nameplate.IsAlive()) {
				// Routes back to the nameplate mod, which destroys the instance.
				_nameplate.Dispose();
			}
			_nameplate = null;

			Reference?.Data?.OnChanged.RemoveListener(OnPlayerDataChanged);

			if (NameplateAnchor != null) {
				NameplateAnchor.gameObject.Destroy();
				NameplateAnchor = null;
			}

			_headBone = null;
			InvalidateHeight();
		}
	}
}
