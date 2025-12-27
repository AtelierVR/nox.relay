namespace Nox.Relay.Core.Types.Quit {
	/// <summary>
	/// Types of disconnections from a session or room.
	/// </summary>
	public enum QuitType : byte {
		/// <summary>
		/// Regular disconnection without any issues.
		/// </summary>
		Normal = 0,

		/// <summary>
		/// Disconnection due to a timeout.
		/// </summary>
		Timeout = 1,

		/// <summary>
		/// Disconnection initiated by moderation action.
		/// </summary>
		ModerationKick = 2,

		/// <summary>
		/// Disconnection initiated by a vote kick.
		/// </summary>
		VoteKick = 3,

		/// <summary>
		/// Disconnection during the connection/preparation phase to the room.
		/// (like error to load mods or world)
		/// </summary>
		ConfigurationError = 4,

		/// <summary>
		/// Disconnection by any other unknown error.
		/// </summary>
		UnknownError = 5,

		/// <summary>
		/// Disconnection due to either a kick.
		/// </summary>
		Kick = ModerationKick | VoteKick
	}
}