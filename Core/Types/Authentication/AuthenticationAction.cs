namespace Nox.Relay.Core.Types.Authentication {
	/// <summary>
	/// Actions related to the authentication process.
	/// Indicates whether to request a challenge or resolve one.
	/// </summary>
	public enum AuthenticationAction : byte {
		/// <summary>
		/// Request a new authentication challenge from the server.
		/// </summary>
		RequestChallenge = 0,

		/// <summary>
		/// Resolve an existing authentication challenge.
		/// </summary>
		ResolveChallenge = 1
	}
}