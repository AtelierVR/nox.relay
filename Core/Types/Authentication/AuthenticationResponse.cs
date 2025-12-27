using System;
using Nox.CCK.Users;
using Buffer = Nox.CCK.Utils.Buffer;
using Nox.Relay.Core.Types.Contents;
using Nox.Users;

namespace Nox.Relay.Core.Types.Authentication {
	/// <summary>
	/// Represents the response to an authentication request.
	/// </summary>
	public class AuthenticationResponse : ContentResponse {
		/// <summary>
		/// The result of the authentication attempt.
		/// </summary>
		public AuthenticationResult Result;

		/// <summary>
		/// The reason for authentication failure, if applicable.
		/// It can be null/empty.
		/// </summary>
		public string Reason;

		/// <summary>
		/// The user identifier assigned upon successful authentication.
		/// </summary>
		public IUserIdentifier Identifier;

		/// <summary>
		/// The display name assigned upon successful authentication.
		/// </summary>
		public string Display;

		/// <summary>
		/// The expiration date and time for a blacklisted user.
		/// If the expiration is epoches, the blacklist is permanent.
		/// </summary>
		public DateTime ExpireAt;

		/// <summary>
		/// The challenge bytes sent by the server for challenge-response authentication.
		/// You need to resolve with <see cref="AuthenticationRequest"/> using these bytes.
		/// </summary>
		public byte[] Challenge;

		/// <summary>
		/// Indicates whether the authentication response represents an error.
		/// </summary>
		public bool IsError
			=> Result     != AuthenticationResult.Success
				&& Result != AuthenticationResult.Challenge;

		/// <summary>
		/// Creates an unknown authentication response with the specified reason.
		/// </summary>
		/// <param name="reason"></param>
		/// <returns></returns>
		public static AuthenticationResponse CreateUnknown(string reason)
			=> new() {
				Result = AuthenticationResult.Unknown,
				Reason = reason
			};

		public override bool FromBuffer(Buffer buffer) {
			buffer.Start();
			Result = buffer.ReadEnum<AuthenticationResult>();
			switch (Result) {
				case AuthenticationResult.Challenge:
					Challenge = buffer.ReadBytes(buffer.ReadByte());
					return true;
				case AuthenticationResult.Invalid:
				case AuthenticationResult.NodeError:
				case AuthenticationResult.Signature:
				case AuthenticationResult.Unknown:
					Reason = buffer.Remaining >= 2
						? buffer.ReadString()
						: "Unknown error";
					return true;
				case AuthenticationResult.Success:
					Identifier = new UserIdentifier(buffer.ReadUInt(), buffer.ReadString());
					if (!Identifier.IsValid()) return false;
					Display = buffer.ReadString();
					return true;
				case AuthenticationResult.Blacklisted:
					ExpireAt = buffer.ReadDateTime();
					Reason   = buffer.ReadString();
					return true;
				default:
					return false;
			}
		}
	}
}