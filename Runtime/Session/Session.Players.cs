using Cysharp.Threading.Tasks;
using Nox.CCK.Sessions;
using Nox.CCK.Utils;
using Nox.Players;
using Nox.Relay.Core.Players;
using Nox.Relay.Core.Types.Enter;
using Nox.Relay.Core.Types.Join;
using Nox.Relay.Core.Types.Leave;
using Nox.Relay.Core.Types.Quit;
using Nox.Relay.Core.Types.Traveling;
using Nox.Relay.Runtime.Players;
using UnityEngine.Events;
using Logger = Nox.CCK.Utils.Logger;
using Player = Nox.Relay.Runtime.Players.Player;

namespace Nox.Relay.Runtime {
	/// <summary>Player join/enter/leave/quit handling, authority transfer and visibility.</summary>
	public sealed partial class Session {
		public UnityEvent<IPlayer> OnPlayerJoined { get; } = new();
		public UnityEvent<IPlayer> OnPlayerLeft { get; } = new();
		public UnityEvent<IPlayer, IPlayer> OnAuthorityTransferred { get; } = new();

		public UnityEvent<IPlayer, bool> OnPlayerVisibility { get; } = new();

		internal void OnPlayerQuitedHandler(QuitEvent @event) {
			Logger.LogDebug($"OnQuit: {@event}", tag: Tag);
			var player = InterEntities.LocalPlayer;
			if (player == null) {
				Logger.LogWarning("Local player not found for Quit event", tag: Tag);
				return;
			}

			player.OnQuit();
			OnPlayerLeftOrQuitedHandler(player);
		}

		internal void OnPlayerJoinedHandler(JoinEvent @event) {
			Logger.LogDebug($"OnJoin: {@event} {@event.Player.Flags}", tag: Tag);
			var player = new RemotePlayer(InterEntities, @event.Player) {
				Platform = @event.Platform.GetPlatformFromName(),
				Engine   = @event.Engine.GetEngineFromName()
			};

			if (player.IsLocal)
				player.Respawn();

			player.OnJoined();
			OnPlayerJoinedOrEnteredHandler(player);
		}

		private void OnPlayerJoinedOrEnteredHandler(Player player) {
			if (player.Reference.Flags.HasFlag(PlayerFlags.RoomMaster))
				InterEntities.MasterId = player.Id;

			if (player.IsLocal)
				player.Respawn();

			Main.CoreAPI.EventAPI.Emit("session_player_joined", this, player);
			OnPlayerJoined.Invoke(player);

			foreach (var module in GetAllModules())
				module.OnPlayerJoined(player);
		}

		private void OnPlayerLeftOrQuitedHandler(Player player) {
			if (InterEntities.MasterId == player.Id)
				InterEntities.MasterId = Nox.Relay.Runtime.Entities.InvalidEntityId;

			Main.CoreAPI.EventAPI.Emit("session_player_left", this, player);
			OnPlayerLeft.Invoke(player);

			foreach (var module in GetAllModules())
				module.OnPlayerLeft(player);

			player.Dispose();
		}

		internal void OnPlayerEnteredHandler(EnterResponse @event)
			=> OnPlayerEnteredHandler(@event, true);

		internal void OnPlayerEnteredHandler(EnterResponse @event, bool travel) {
			Logger.LogDebug($"OnEnter: {@event}", tag: Tag);
			var player = new LocalPlayer(InterEntities, @event.Player);
			InterEntities.LocalId = player.Id;

			Room                        = @event.Room;
			Room.Tps                    = @event.Tps;
			_lastKnownTps               = @event.Tps;
			Room.Threshold              = @event.Threshold;
			Room.RenderEntity           = @event.RenderEntity;
			Room.PropertyResendInterval = @event.PropertyResendInterval;

			// The room owns part of the session info (player capacity).
			HookRoomInfo();

			// Initialize room voice routing (idempotent)

			SetupVoiceRouting();

			if (travel)
				Room.Traveling(TravelingRequest.Travel()).Forget();

			player.OnEntered();
			OnPlayerJoinedOrEnteredHandler(player);

			if (SessionHelper.IsCurrent(Main.SessionAPI, this))
				OnControllerChanged(Main.ControllerAPI.Current);
		}

		internal void OnPlayerLeftHandler(LeaveEvent @event) {
			Logger.LogDebug($"OnLeave: {@event}", tag: Tag);
			var player = InterEntities.GetEntity<RemotePlayer>(@event.PlayerId);
			if (player == null) {
				Logger.LogWarning($"Player with ID {@event.PlayerId} not found for Leave event");
				return;
			}

			player.OnLeft();
			OnPlayerLeftOrQuitedHandler(player);
		}

		public void OnAuthorityTransferredHandler(Player @new, Player old) {
			Logger.LogDebug($"OnAuthorityTransferred: {old} -> {@new}", tag: Tag);

			Main.CoreAPI.EventAPI.Emit("session_authority_transferred", this, @new, old);
			OnAuthorityTransferred.Invoke(@new, old);

			foreach (var module in GetAllModules())
				module.OnAuthorityTransferred(@new, old);
		}

		public void OnPlayerVisibilityChangedHandler(IPlayer player, bool isVisible) {
			Logger.LogDebug($"OnPlayerVisibilityChanged: {player} is now {(isVisible ? "visible" : "invisible")}", tag: Tag);

			Main.CoreAPI.EventAPI.Emit("session_player_visibility_changed", this, player, isVisible);
			OnPlayerVisibility.Invoke(player, isVisible);

			foreach (var module in GetAllModules())
				module.OnPlayerVisibilityChanged(player, isVisible);
		}
	}
}
