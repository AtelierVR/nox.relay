using Nox.CCK.Sessions;
using Nox.CCK.Utils;
using Nox.Relay.Runtime.Physicals;
using UnityEngine;
using Logger = Nox.CCK.Utils.Logger;
using Object = UnityEngine.Object;

namespace Nox.Relay.Runtime.Players {
	/// <summary>
	/// Physical representation of a remote player: visibility culling, prefab instantiation and
	/// the create/destroy hooks that drive the voice provider.
	/// </summary>
	public partial class RemotePlayer {

		override protected bool IsVisible
			=> Vector3.Distance(Position, Context.LocalPlayer.Position)
				< Mathf.Min(Context.Context.Room.RenderEntity, Settings.RenderEntityDistance);

		override protected Physicals.Physical InstantiatePhysical() {
			var asset = Main.CoreAPI.AssetAPI.GetAsset<GameObject>("remote_physical.prefab");
			if (!asset) {
				Logger.LogError("Failed to load remote physical prefab");
				return null;
			}

			var instance = asset.Instantiate();
			instance.name = $"{typeof(RemotePhysical)}_{Id}";

			var physical = instance.GetComponent<RemotePhysical>();
			if (!physical) {
				Logger.LogError("Remote physical prefab is missing RemotePhysical component");
				instance.Destroy();
				return null;
			}

			physical.Reference = this;
			Object.DontDestroyOnLoad(instance);
			return physical;
		}

		/// <summary>
		/// Called after the physical representation is created.
		/// </summary>
		/// <remarks>
		/// The avatar itself is applied by <see cref="Physicals.RemotePhysical.Setup"/>, which runs
		/// as soon as <c>Reference</c> is assigned in <see cref="InstantiatePhysical"/> (and on
		/// enable): it shows the error avatar when the announced identifier is unusable, otherwise
		/// the loading placeholder followed by the announced avatar once it is resolved.
		/// </remarks>
		override protected void OnPhysicalCreated() {
			base.OnPhysicalCreated();

			// Set up voice on the new physical
			VoiceProvider.Initialize();
		}

		override protected void OnPhysicalDestroyed() {
			VoiceProvider.Dispose();
			base.OnPhysicalDestroyed();
		}

	}
}
