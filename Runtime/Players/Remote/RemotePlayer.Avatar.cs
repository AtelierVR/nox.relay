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
				Logger.LogDebug($"No physical yet for RemotePlayer {Id}, avatar will be set when physical is created", tag: nameof(RemotePlayer));
				return true;
			}

			if (Physical is not RemotePhysical physical)
				return false;

			var result = await physical.SetAvatar(identifier);
			if (result == null)
				return false;

			StartVoice();

			return true;
		}

	}
}
