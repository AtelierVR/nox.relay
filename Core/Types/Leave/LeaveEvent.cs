using Nox.Relay.Core.Types.Content.Rooms;
using Nox.Relay.Core.Types.Quit;
using Buffer = Nox.CCK.Utils.Buffer;

namespace Nox.Relay.Core.Types.Leave {
	/// <summary>
	/// Event representing a player leaving a room.
	/// </summary>
	public class LeaveEvent : RoomResponse {
		/// <summary>
		/// Type of quit event.
		/// </summary>
		public QuitType Type;

		/// <summary>
		/// ID of the player who left.
		/// </summary>
		public ushort PlayerId;

		/// <summary>
		/// ID of the moderator or player who initiated the leave, if applicable.
		/// Is viewable only if you're a moderator.
		/// </summary>
		public ushort ByPlayerId;

		/// <summary>
		/// Reason for the leave, if provided.
		/// Is viewable only if you're a moderator.
		/// </summary>
		public string Reason;

		/// <summary>
		/// Indicates if you can view moderation information.
		/// </summary>
		public bool HasModerator
			=> ByPlayerId != ushort.MaxValue;

		public override bool FromBuffer(Buffer buffer) {
			buffer.Start();

			Type     = buffer.ReadEnum<QuitType>();
			PlayerId = buffer.ReadUShort();

			// Check if there's additional data for moderation actions
			if (buffer.Remaining == 0)
				return true;

			ByPlayerId = buffer.ReadUShort();

			if (buffer.Remaining > 0)
				Reason = buffer.ReadString();

			return true;
		}

		public override string ToString()
			=> $"{GetType().Name}[Iid={Room.InternalId}, Type={Type}, PlayerId={PlayerId}, ByPlayerId={ByPlayerId}, Reason={Reason}]";
	}
}