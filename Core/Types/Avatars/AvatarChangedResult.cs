namespace Nox.Relay.Core.Types.Avatars {
	/// <summary>
	/// Result of an avatar change operation.
	/// </summary>
	public enum AvatarChangedResult : byte {
		/// <summary>
		/// Avatar of a player is currently changing.
		/// </summary>
		Changing = 0,
		/// <summary>
		/// Avatar change operation resulted in an unknown state.
		/// </summary>
		Unknown = 1,
		/// <summary>
		/// Avatar change operation failed.
		/// </summary>
		Failed = 2,
		/// <summary>
		/// Avatar change operation succeeded.
		/// </summary>
		Success = 3
	}
}