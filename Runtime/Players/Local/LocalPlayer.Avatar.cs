using Cysharp.Threading.Tasks;
using Nox.Avatars;
using Nox.Relay.Core.Types.Avatars;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Relay.Runtime.Players {
	public partial class LocalPlayer {

		/// <summary>Announces the avatar to the room, then binds its parameter module.</summary>
		internal override void UpdateAvatar(IRuntimeAvatar avatar)
			=> UpdateAvatarAsync(avatar).Forget();

		private async UniTask UpdateAvatarAsync(IRuntimeAvatar avatar) {
			if (avatar == null || !avatar.Identifier.IsValid()) {
				if (avatar != null)
					Logger.LogDebug($"Skipping avatar announcement for {avatar} (invalid identifier {avatar.Identifier.ToString()}).", tag: GetType().Name);
				base.UpdateAvatar(null);
				return;
			}

			var response = await Context.Context.Room.ChangeAvatar(AvatarChangedRequest.Self(avatar.Identifier));
			if (response.IsError) {
				Logger.LogWarning($"Failed to change avatar: {response.Reason}");
				return;
			}

			base.UpdateAvatar(avatar);
		}

	}
}
