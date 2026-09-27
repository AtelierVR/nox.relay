using Cysharp.Threading.Tasks;
using Nox.Relay.Core.Types.Avatars;
using Player = Nox.Relay.Runtime.Players.Player;

namespace Nox.Relay.Runtime {
	/// <summary>Avatar change requests received from the room.</summary>
	public sealed partial class Session {
		public void OnAvatarChanged(AvatarChangedEvent @event)
			=> OnAvatarChangedAsync(@event).Forget();

		private async UniTask OnAvatarChangedAsync(AvatarChangedEvent @event) {
			if (@event.Result != AvatarChangedResult.Changing)
				return;
			var player = InterEntities.GetEntity<Player>(@event.PlayerId);
			if (player == null)
				return;
			await player.SetAvatar(@event.Identifier);
		}
	}
}
