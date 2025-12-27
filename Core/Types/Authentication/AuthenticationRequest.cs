using Nox.CCK.Utils;
using Nox.Relay.Core.Types.Contents;
using Nox.Users;

namespace Nox.Relay.Core.Types.Authentication {
	/// <summary>
	/// Represents a request for authentication in the relay system.
	/// <para/>
	/// This request can either be for requesting a challenge or resolving a challenge.
	/// It contains the necessary data depending on the action being performed.
	/// </summary>
	public class AuthenticationRequest : ContentRequest {
		/// <summary>
		/// The action to be performed in the authentication process.
		/// </summary>
		public AuthenticationAction Action;

		/// <summary>
		/// Client's public key used for requesting a challenge.
		/// Is included when <see cref="Action"/> is <see cref="AuthenticationAction.RequestChallenge"/>.
		/// The public key is generated when the client creates a new key pair and auth with their node server.
		/// </summary>
		public byte[] PublicKey; // Client's public key (for RequestChallenge)

		/// <summary>
		/// Signature of the challenge signed by the client's private key.
		/// Is included when <see cref="Action"/> is <see cref="AuthenticationAction.ResolveChallenge"/>.
		/// The signature is created by signing the challenge received from the server using the client's private key.
		/// </summary>
		public byte[] Signature; // Signature of the challenge (for ResolveChallenge)

		/// <summary>
		/// User identity of the client attempting to authenticate.
		/// Is included when <see cref="Action"/> is <see cref="AuthenticationAction.ResolveChallenge"/>.
		/// </summary>
		public IUserIdentifier Identifier;
		
		/// <summary>
		/// Creates a new <see cref="AuthenticationRequest"/> for requesting a challenge.
		/// </summary>
		/// <returns></returns>
		public static AuthenticationRequest Request()
			=> new() { Action = AuthenticationAction.RequestChallenge };

		/// <summary>
		/// Creates a new <see cref="AuthenticationRequest"/> for resolving a challenge.
		/// </summary>
		/// <param name="publicKey"></param>
		/// <param name="signature"></param>
		/// <param name="identifier"></param>
		/// <returns></returns>
		public static AuthenticationRequest Resolve(byte[] publicKey, byte[] signature, IUserIdentifier identifier)
			=> new() {
				Action     = AuthenticationAction.ResolveChallenge,
				PublicKey  = publicKey,
				Signature  = signature,
				Identifier = identifier
			};

		public override Buffer ToBuffer() {
			var buffer = new Buffer();
			buffer.Write(Action);
			if (Action != AuthenticationAction.ResolveChallenge)
				return buffer;
			buffer.Write((ushort)PublicKey.Length);
			buffer.Write(PublicKey);
			buffer.Write((ushort)Signature.Length);
			buffer.Write(Signature);
			buffer.Write(Identifier.GetId());
			buffer.Write(Identifier.GetServer());
			return buffer;
		}
	}
}