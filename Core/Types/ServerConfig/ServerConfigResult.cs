namespace Nox.Relay.Core.Types.ServerConfig {
	/// <summary>
	/// Result code returned in a <see cref="ServerConfigResponse"/>.
	/// </summary>
	public enum ServerConfigResult : byte {
		/// <summary>The configuration change was applied successfully.</summary>
		Success = 0,

		/// <summary>The configuration change was rejected (insufficient privileges or invalid data).</summary>
		Failure = 1,

		/// <summary>
		/// Broadcast sent to all players when a setting changes.
		/// The client should update its cached instance configuration accordingly.
		/// </summary>
		Change = 2,
	}
}
