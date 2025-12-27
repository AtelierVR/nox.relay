using System;
using Nox.Relay.Core.Rooms;
using Nox.Users;

namespace Nox.Relay.Core.Players {
	/// <summary>
	/// Represents a player connected to a room.
	/// </summary>
	public class Player {
		/// <summary>
		/// The room the player is connected to.
		/// </summary>
		public Room Room;

		/// <summary>
		/// The comportment flags of the player.
		/// </summary>
		public PlayerFlags Flags;

		/// <summary>
		/// The unique identifier of the player within the room.
		/// </summary>
		public ushort Id;

		/// <summary>
		/// The user identifier of the player.
		/// Is can be Invalid if the player is a guest.
		/// </summary>
		public IUserIdentifier Identifier;

		/// <summary>
		/// The display name of the player.
		/// </summary>
		public string   Display;
		
		/// <summary>
		/// The time when the player joined the room.
		/// </summary>
		public DateTime JoinedAt;
	}
}