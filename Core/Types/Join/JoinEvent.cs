using Nox.CCK.Users;
using Nox.Relay.Core.Players;
using Nox.Relay.Core.Types.Content.Rooms;
using Buffer = Nox.CCK.Utils.Buffer;

namespace Nox.Relay.Core.Types.Join {
	/// <summary>
	/// Event representing a player joining a room.
	/// </summary>
	public class JoinEvent : RoomResponse {
		/// <summary>
		/// The player who has joined the room.
		/// </summary>
		public Player Player;

		/// <summary>
		/// The game engine the player is using.
		/// for more details, see <see cref="Engine"/>.
		/// </summary>
		public string Engine;

		/// <summary>
		/// The platform the player is using.
		/// for more details, see <see cref="Platform"/>.
		/// </summary>
		public string Platform;

		public override bool FromBuffer(Buffer buffer) {
			buffer.Start();

			Player = new Player {
				Room       = Room,
				Flags      = buffer.ReadEnum<PlayerFlags>(),
				Id         = buffer.ReadUShort(),
				Identifier = new UserIdentifier(buffer.ReadUInt(), buffer.ReadString()),
				Display    = buffer.ReadString(),
				JoinedAt   = buffer.ReadDateTime()
			};

			Engine   = buffer.ReadString();
			Platform = buffer.ReadString();

			return true;
		}
		
		public override string ToString()
			=> $"{GetType().Name}[Iid={Room.InternalId}, Player={Player?.ToString() ?? "null"}, Engine={Engine}, Platform={Platform}]";
	}
}