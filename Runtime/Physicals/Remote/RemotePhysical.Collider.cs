using Nox.CCK.Utils;
using Nox.Entities;
using UnityEngine;

namespace Nox.Relay.Runtime.Physicals {
	/// <summary>
	/// Body capsule of a remote player, fitted to the size of its avatar: the capsule of the desktop proxy is
	/// driven by the height of the player (<c>DesktopPlayer.UpdatePlayerHeight</c>), and a remote avatar is
	/// not the prefab size either - it is scaled by the height its owner publishes.
	/// <para>
	/// The height comes from the synced <c>Height</c> parameter of the avatar (the same source as the
	/// nameplate), so the capsule follows the avatar of this client and its crouch. Nothing is written while
	/// the capsule already matches, and every write is announced: the parameter module registering, the avatar
	/// being attached or swapped, and the property carrying the height being applied.
	/// </para>
	/// </summary>
	public partial class RemotePhysical {
		/// <summary>Avatar parameter carrying the height of the player.</summary>
		private const string HEIGHT_PARAMETER = "Height";

		/// <summary>Avatar parameter carrying the height of the player, when the avatar has no <c>Height</c>.</summary>
		private const string EYE_HEIGHT_PARAMETER = "Height/Eye";

		/// <summary>
		/// Avatar parameter carrying the scale the owner applies to its avatar. The height parameters above are
		/// local only: this is the one a viewer receives, and the height of the avatar follows it.
		/// </summary>
		private const string SCALE_PARAMETER = "Scale";

		/// <summary>Capsule standing for the body of the player, on the root of the physical.</summary>
		private CapsuleCollider _bodyCollider;

		private CapsuleCollider BodyCollider
			=> _bodyCollider ??= GetComponent<CapsuleCollider>();

		/// <summary>
		/// Sizes the capsule of the body from the avatar: height of the player, centred on the root of the
		/// physical so the feet stay on the floor.
		/// </summary>
		private void UpdateBodyCollider() {
			var collider = BodyCollider;
			if (!collider)
				return;

			var height = ResolveHeight();
			if (height <= 0f)
				return;

			height = Mathf.Clamp(height, Constants.MINIMUM_BODY_HEIGHT, Constants.MAXIMUM_BODY_HEIGHT);

			var center = collider.center;
			if (Mathf.Approximately(collider.height, height) && Mathf.Approximately(center.y, height * 0.5f))
				return;

			collider.height = height;
			collider.center = new Vector3(center.x, height * 0.5f, center.z);
		}

		/// <summary>
		/// Follows what changes the size of the body: the properties the owner publishes, the height being one
		/// of them (it lands on the avatar parameter, without announcing a value change of its own).
		/// </summary>
		private void BindBodySize() {
			var player = Reference;
			if (player == null)
				return;

			player.OnProperty.RemoveListener(OnBodyProperty);
			player.OnProperty.AddListener(OnBodyProperty);

			UpdateBodyCollider();
		}

		private void UnbindBodySize() {
			var player = Reference;
			if (player == null)
				return;

			player.OnProperty.RemoveListener(OnBodyProperty);
		}

		/// <summary>A property of the owner was applied: the size of the avatar is carried by one of them.</summary>
		private void OnBodyProperty(IProperty property) {
			if (!IsBodySizeParameter(property?.Name))
				return;

			InvalidateHeight();
			UpdateBodyCollider();
		}

		/// <summary>Whether a parameter name carries the size of the avatar.</summary>
		private static bool IsBodySizeParameter(string name)
			=> name == HEIGHT_PARAMETER
				|| name == EYE_HEIGHT_PARAMETER
				|| name == SCALE_PARAMETER;
	}
}
