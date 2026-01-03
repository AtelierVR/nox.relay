using CorePlayer = Nox.Relay.Core.Players.Player;

namespace Nox.Relay.Runtime.Players {
	public class RemotePlayer : Player {
		public RemotePlayer(Entities context, CorePlayer player) : base(context, player) { }
	}
}