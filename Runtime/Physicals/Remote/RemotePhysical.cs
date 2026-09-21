using Cysharp.Threading.Tasks;
using Nox.Avatars.Rigging;
using Nox.CCK.Utils;
using Nox.Relay.Runtime.Players;
using UnityEngine;

namespace Nox.Relay.Runtime.Physicals {
	public partial class RemotePhysical : Physical {

		/// <summary>Fired when a new avatar is fully set up on this physical. Listeners may migrate voice/camera sources.</summary>
		public new RemotePlayer Reference {
			get => (RemotePlayer)base.Reference;
			set {
				base.Reference = value;
				Setup().Forget();
			}
		}

		private Rigidbody _rigidbody;
		private IRigProvider _rigProvider;

		private new Rigidbody rigidbody
			=> _rigidbody ??= gameObject.GetOrAddComponent<Rigidbody>();

		override protected void OnEnable() {
			base.OnEnable();
			Setup().Forget();
		}

		override protected void OnDisable() {
			CancelAvatarLoading();
			base.OnDisable();
		}
	}
}
