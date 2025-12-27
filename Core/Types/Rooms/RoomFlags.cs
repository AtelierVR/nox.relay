using System;

namespace Nox.Relay.Core.Types.Rooms {
	/// <summary>
	/// Flags that define room properties and behaviors.
	/// </summary>
	public enum RoomFlags : uint {
		None = 0,

		/// <summary>
		/// Indicates that the room is public
		/// and is visible to all users by <see cref="RequestType.Rooms"/>
		/// </summary>
		IsPublic = 1 << 0,

		/// <summary>
		/// Indicates that the room requires a password for access.
		/// </summary>
		UsePassword = 1 << 1,

		/// <summary>
		/// Indicates that the room uses a whitelist
		/// to restrict access to specific users only.
		/// </summary>
		UseWhitelist = 1 << 2,

		/// <summary>
		/// Indicates that the room authorizes bot connections.
		/// Is used to differentiate players and bots.
		/// </summary>
		[Obsolete("Bots are now authorized via the authentication system.")]
		AuthorizeBot = 1 << 3,

		/// <summary>
		/// Indicates that the room allows overload connections.
		/// Overload connections permit more users than the standard limit.
		/// </summary>
		AllowOverload = 1 << 4,
	}
}