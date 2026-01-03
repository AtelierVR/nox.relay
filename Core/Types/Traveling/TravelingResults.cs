using System;

namespace Nox.Relay.Core.Types.Traveling {
	/// <summary>
	/// Results of a traveling attempt.
	/// </summary>
	[Flags]
	public enum TravelingResults : byte {
		None = 0,

		/// <summary>
		/// Use a downloaded URL to travel.
		/// </summary>
		UseUrl = 1 << 1,

		/// <summary>
		/// Use a <see cref="Nox.Worlds.IWorldIdentifier"/> to travel.
		/// </summary>
		UseNode = 1 << 2,

		/// <summary>
		/// Use a sha256 hash to travel.
		/// </summary>
		UseHash = 1 << 3,

		/// <summary>
		/// An unknown error occurred during traveling.
		/// </summary>
		Unknown = 1 << 4,

		/// <summary>
		/// The traveling is ready to proceed.
		/// </summary>
		Ready = 1 << 5,
		
		/// <summary>
		/// Password is linked with the Use flag to load the dimension.
		/// </summary>
		HasPassword = 1 << 6
	}
}