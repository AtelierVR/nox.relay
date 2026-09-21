using Cysharp.Threading.Tasks;
using Nox.CCK.Utils;
using Nox.Relay.Runtime.Physicals;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Relay.Runtime.Players {
	/// <summary>
	/// Avatar handling for a remote player: announces the avatar change through the physical and
	/// binds the entity's avatar parameter properties to the attached avatar's parameters.
	/// </summary>
	public partial class RemotePlayer {

		public override async UniTask<bool> SetAvatar(Identifier identifier) {
			Logger.LogDebug($"Changing avatar for {this} to {identifier.ToString()}", tag: nameof(RemotePlayer));

			Avatar = identifier;

			if (!HasPhysical()) {
				// No physical yet, but we still need to prepare properties for when avatar loads
				Logger.LogDebug($"No physical yet for RemotePlayer {Id}, avatar will be set when physical is created", tag: nameof(RemotePlayer));
				return true;
			}

			if (Physical is not RemotePhysical physical)
				return false;

			var result = await physical.SetAvatar(identifier);
			if (result == null)
				return false;

			// The avatar's parameter properties were declared by RemotePhysical.SetAvatar(IRuntimeAvatar)
			// when it attached the avatar — binding again here would duplicate the sync pass.

			// VoiceProvider.Initialize() is a one-shot (it returns immediately once Started), so this
			// is not an avatar-change hook: it only covers the case where the first attempt — from
			// OnPhysicalCreated() — bailed out with "Physical not ready". The avatar-driven output
			// migration is done by the provider itself, which listens to this player's OnAvatarLoaded
			// (RemoteVoiceProvider.OnAvatarLoaded → CreateOrMigrateOutput), the same attach point that
			// now declares the avatar parameters.
			VoiceProvider.Initialize();

			return true;
		}

	}
}
