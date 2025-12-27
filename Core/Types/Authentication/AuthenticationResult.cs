namespace Nox.Relay.Core.Types.Authentication {
	/// <summary>
	/// Result of an authentication attempt.
	/// </summary>
	public enum AuthenticationResult : byte {
		/// <summary>
		/// Authentication was successful.
		/// </summary>
		Success = 0,

		/// <summary>
		/// The relay needs to challenge the client for further verification.
		/// </summary>
		Challenge = 1,

		/// <summary>
		/// An error occurred on the node of the relay is not functioning properly.
		/// </summary>
		NodeError = 2,

		/// <summary>
		/// The client is blacklisted and cannot connect.
		/// This result can be used with a expiration time and reason in the authentication response.
		/// </summary>
		Blacklisted = 3,

		/// <summary>
		/// The authentication data provided by the client is invalid.
		/// </summary>
		Invalid = 4,

		/// <summary>
		/// The signature provided by the client is invalid.
		/// </summary>
		Signature = 5,

		/// <summary>
		/// An unknown error occurred during authentication.
		/// </summary>
		Unknown = 255
	}
}