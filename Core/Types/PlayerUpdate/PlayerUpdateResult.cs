namespace Nox.Relay.Core.Types.PlayerUpdate {
	/// <summary>
	/// Result code returned in a <see cref="PlayerUpdateResponse"/>.
	/// </summary>
	public enum PlayerUpdateResult : byte {
		/// <summary>The update was applied successfully by the requester.</summary>
		Success = 0,

		/// <summary>The update was rejected (insufficient privileges or invalid data).</summary>
		Failure = 1,

		/// <summary>
		/// A broadcast notifying other players that a field changed.
		/// Sent by the server when a peer's player data is updated.
		/// </summary>
		Change = 2,
	}
}
