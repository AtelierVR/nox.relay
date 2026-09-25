using Cysharp.Threading.Tasks;
using Nox.Avatars.Parameters;
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

        /// <summary>
        /// Raised by the bound player when the avatar module registers/unregisters a parameter.
        /// </summary>
        override public void OnParameterChanged(IParameter parameter, bool added) {
            // The nameplate places itself from the avatar Height: react as soon as that parameter
            // lands, instead of polling for it to appear.
            OnNameplateParameterChanged(parameter);
        }
	}
}
