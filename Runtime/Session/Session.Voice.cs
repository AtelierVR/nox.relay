using System.Collections.Generic;
using System.Linq;
using Nox.CCK.Events;
using Nox.Players;
using Nox.Relay.Core.Types.Stream;
using Nox.Relay.Runtime.Players;
using Player = Nox.Relay.Runtime.Players.Player;

namespace Nox.Relay.Runtime {
	/// <summary>
	/// Voice chat routing. Audio arrives through <see cref="Core.Rooms.Room.OnStream"/>
	/// (<c>PacketType.Stream</c> datagrams); the session only redirects each packet to the player
	/// it belongs to. The per-player handling lives in <c>Player.Voice.cs</c> and its
	/// <c>LocalPlayer.Voice.cs</c> / <c>RemotePlayer.Voice.cs</c> partials.
	/// </summary>
	public sealed partial class Session {
		/// <summary>Subscribes to the room's stream reception and voice event interception (idempotent).</summary>
		private void SetupVoiceRouting() {
			if (Room == null)
				return;

			Room.OnStream.RemoveListener(OnStreamReceived);
			Room.OnStream.AddListener(OnStreamReceived);

			// The private relations of voice (a per-listener mute) are intercepted here, before
			// they reach the public events and the modules.
			OnEventIntercept.RemoveListener(OnVoiceEventIntercept);
			OnEventIntercept.AddListener(OnVoiceEventIntercept);
		}

		/// <summary>Detaches the room's stream reception and voice event interception.</summary>
		private void DisposeVoiceRouting() {
			Room?.OnStream.RemoveListener(OnStreamReceived);
			OnEventIntercept.RemoveListener(OnVoiceEventIntercept);
		}

		/// <summary>
		/// Redirects a voice packet to the player it belongs to: the speaker for both an audio
		/// <see cref="StreamSubType.Sample"/> and a <see cref="StreamSubType.Control"/>, since a
		/// control gates the playback of the speaker it names.
		/// </summary>
		private void OnStreamReceived(StreamEvent @event) {
			var target = @event.SubType switch {
				StreamSubType.Sample  => _entities.GetEntity<Player>(@event.PlayerId),
				StreamSubType.Control => _entities.GetEntity<Player>(@event.SpeakerId),
				_                     => null
			};

			target?.OnVoiceStream(@event);
		}

		#region Mute relations

		// A listener mute is private: the muted player is told through a targeted relay event,
		// never through the room-wide properties. Only the two involved players know.

		/// <summary>Raised with the players muting the local user whenever that set changes.</summary>
		public readonly NoxEvent<IPlayer[]> OnMutedByChanged = new();

		/// <summary>True when at least one player muted the local user.</summary>
		public bool IsMutedByAnyone
			=> _entities.GetEntities<RemotePlayer>().Any(p => p.MutesLocalPlayer);

		/// <summary>
		/// Players that muted the local user, ordered by id. A display name is not an identity (two
		/// players may share one), so the players themselves are exposed, never just their names.
		/// </summary>
		public IReadOnlyList<IPlayer> MutedBy
			=> _entities.GetEntities<RemotePlayer>()
				.Where(p => p.MutesLocalPlayer)
				.OrderBy(p => p.Id)
				.Cast<IPlayer>()
				.ToList();

		/// <summary>
		/// Consumes the private "I muted you" event before it reaches the public events: the relation
		/// belongs to the remote player that sent it, and is never exposed room-wide.
		/// </summary>
		private void OnVoiceEventIntercept(EventDispatchContext context) {
			if (context.Name != Player.VoiceMuteEventKey)
				return;

			context.Handled = true;

			if (context.Sender is RemotePlayer remote && context.Payload is { Length: > 0 } payload)
				remote.SetMutesLocalPlayer(payload[0] != 0);
		}

		/// <summary>Raised by a remote player whenever its private mute relation to us changes.</summary>
		private void OnRemoteMutesLocalPlayerChanged(bool muted)
			=> PublishMutedBy();

		/// <summary>Re-sends the mutes the local user applies to every remote currently in the room.</summary>
		internal void NotifyMutedRemotes() {
			foreach (var remote in _entities.GetEntities<RemotePlayer>())
				if (remote.IsMuted)
					remote.NotifyMuted(true);

			// A HUD created after the last change would miss the current state.
			if (IsMutedByAnyone)
				PublishMutedBy();
		}

		/// <summary>Recomputes and publishes the aggregate "muted by" state (consumed by the HUDs).</summary>
		internal void PublishMutedBy() {
			var players = MutedBy.ToArray();

			OnMutedByChanged.Invoke(players);

			// Mods without the players assembly still get the count; those that have it get the
			// identities, so a shared display name is never used as an identity.
			Main.CoreAPI.EventAPI.Emit(
				"voice.muted_by_changed",
				this,
				players.Length > 0,
				players.Length,
				players
			);
		}

		#endregion
	}
}
