using System;

namespace Nox.Relay.Core.Players {
	/// <summary>
	/// Flags representing various statuses and roles of a player within the relay system.
	/// </summary>
	[Flags]
	public enum PlayerFlags : uint {
		None = 0,

		[Obsolete("Bots are no longer supported.")]
		IsBot = 1 << 0,

		/// <summary>
		/// Player is the master of the room.
		/// </summary>
		RoomMaster = 1 << 1,

		/// <summary>
		/// Player is a moderator in the room.
		/// He can kick/ban other players from the room.
		/// </summary>
		RoomModerator = 1 << 2,

		/// <summary>
		/// Initial creator of the room.
		/// Has special privileges in the room.
		/// </summary>
		RoomOwner = 1 << 3,

		/// <summary>
		/// If the room is guild affiliated, player is a guild moderator.
		/// He can moderate like <see cref="RoomModerator"/>.
		/// </summary>
		GuildModerator = 1 << 4,

		/// <summary>
		/// Player is a moderator on the node level.
		/// He can moderate rooms and players across the entire node.
		/// </summary>
		NodeModerator = 1 << 5,

		/// <summary>
		/// Player is the owner of the world used by the room.
		/// He has special privileges in the world.
		/// </summary>
		WorldOwner = 1 << 6,

		/// <summary>
		/// Additional world moderator to help the <see cref="WorldOwner"/>.
		/// He can moderate players in the world.
		/// </summary>
		WorldModerator = 1 << 7,

		/// <summary>
		/// Player has not completed authentication verification.
		/// This flag is used to indicate that the player has connected but not yet verified.
		/// </summary>
		AuthUnverified = 1 << 8,

		/// <summary>
		/// Player is hidden from public player lists.
		/// He will not appear in the node's player list.
		/// </summary>
		HideInList = 1 << 9,

		/// <summary>
		/// Player is owner.
		/// </summary>
		IsOwner = RoomOwner | WorldOwner | NodeModerator,

		/// <summary>
		/// Player is moderator.
		/// </summary>
		IsModerator = RoomModerator | GuildModerator | NodeModerator | WorldModerator,
	}
}