using Nox.Relay.Core.Types.Stream;
using Player = Nox.Relay.Runtime.Players.Player;

namespace Nox.Relay.Runtime {
	/// <summary>
	/// Voice chat routing. Audio arrives through <see cref="Core.Rooms.Room.OnStream"/>
	/// (<c>PacketType.Stream</c> datagrams); the session only redirects each packet to the player
	/// it belongs to. The per-player handling lives in <c>Player.Voice.cs</c> and its
	/// <c>LocalPlayer.Voice.cs</c> / <c>RemotePlayer.Voice.cs</c> partials.
	/// </summary>
	public sealed partial class Session {
		/// <summary>Subscribes to the room's stream reception (idempotent).</summary>
		private void SetupVoiceRouting() {
			if (Room == null)
				return;

			Room.OnStream.RemoveListener(OnStreamReceived);
			Room.OnStream.AddListener(OnStreamReceived);
		}

		/// <summary>Detaches the room's stream reception.</summary>
		private void DisposeVoiceRouting()
			=> Room?.OnStream.RemoveListener(OnStreamReceived);

		/// <summary>
		/// Redirects a voice packet to the player it belongs to: the speaker for both an audio
		/// <see cref="StreamSubType.Sample"/> and a <see cref="StreamSubType.Control"/>, since a
		/// control gates the playback of the speaker it names.
		/// </summary>
		private void OnStreamReceived(StreamEvent @event) {
			var target = @event.SubType switch {
				StreamSubType.Sample  => InterEntities.GetEntity<Player>(@event.PlayerId),
				StreamSubType.Control => InterEntities.GetEntity<Player>(@event.SpeakerId),
				_                     => null
			};

			target?.OnVoiceStream(@event);
		}
	}
}
