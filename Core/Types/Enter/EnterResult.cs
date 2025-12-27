using Nox.Relay.Core.Rooms;

namespace Nox.Relay.Core.Types.Enter {
	/// <summary>
	/// Possible results when attempting to enter a room.
	/// </summary>
	public enum EnterResult : byte {
		/// <summary>
		/// Entering the room was successful.
		/// </summary>
		Success = 0,

		/// <summary>
		/// The room was not found.
		/// You have tried to use a wrong <see cref="Room.InternalId"/>
		/// </summary>
		NotFound = 1,

		/// <summary>
		/// The room is full.
		/// </summary>
		Full = 2,

		/// <summary>
		/// The client is blacklisted from entering the room.
		/// Is different from <see cref="Authentication.AuthenticationResult.Blacklisted"/>
		/// which indicates a blacklist at the node/relay level.
		/// </summary>
		Blacklisted = 3,

		/// <summary>
		/// The client is not whitelisted to enter the room.
		/// This means the room has a whitelist and the client is not on it.
		/// </summary>
		NotWhitelisted = 4,

		/// <summary>
		/// The game associated with the room is invalid or not supported.
		/// </summary>
		InvalidGame = 5,

		/// <summary>
		/// The password provided for the room is incorrect.
		/// </summary>
		IncorrectPassword = 6,

		/// <summary>
		/// An unknown error occurred while attempting to enter the room.
		/// Is joined by a reason message in the response.
		/// </summary>
		Unknown = 7,

		/// <summary>
		/// Unknown is an unexpected error,
		/// whereas refused is a possible error but not yet listed.
		/// Is for custom server-side logic to refuse an entry without specifying why.
		/// </summary>
		Refused = 8,

		/// <summary>
		/// The pseudonyme (display name) provided is invalid.
		/// </summary>
		InvalidPseudonyme = 9
	}
}