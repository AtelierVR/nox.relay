using System;

namespace Nox.Relay.Core.Types.Enter {
	/// <summary>
	/// Flags used when entering a room.
	/// </summary>
	[Flags]
	public enum EnterFlags : byte {
		None = 0,

		/// <summary>
		/// Enter as a bot.
		/// Bots are is not visible to regular users.
		/// </summary>
		[Obsolete("Bots are no longer supported.")]
		AsBot = 1 << 0,

		/// <summary>
		/// Use a pseudonym instead of the real Display Name
		/// (given at <see cref="Authentication.AuthenticationResponse.Display"/>).
		/// </summary>
		UsePseudonyme = 1 << 1,

		/// <summary>
		/// Use a password to enter the room.
		/// </summary>
		UsePassword = 1 << 2,

		/// <summary>
		/// Hide this user in the instance node server list.
		/// </summary>
		HideInList = 1 << 3
	}
}