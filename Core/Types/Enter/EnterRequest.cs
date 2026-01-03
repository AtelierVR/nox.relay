using Nox.Relay.Core.Types.Contents.Rooms;
using Buffer = Nox.CCK.Utils.Buffer;

namespace Nox.Relay.Core.Types.Enter {
	/// <summary>
	/// Request to enter a room with optional pseudonym and password.
	/// </summary>
	public class EnterRequest : RoomRequest {
		/// <summary>
		/// Flags indicating whether a pseudonym, password... are used.
		/// </summary>
		public EnterFlags Flags;

		/// <summary>
		/// The display name (pseudonym) to use when entering the room.
		/// </summary>
		public string Display;

		/// <summary>
		/// The password to use when entering the room.
		/// If the room is not password-protected, this can be null/empty.
		/// </summary>
		public string Password;

		public override Buffer ToBuffer() {
			var buffer = new Buffer();

			if (string.IsNullOrEmpty(Display))
				Flags  &= ~EnterFlags.UsePseudonyme;
			else Flags |= EnterFlags.UsePseudonyme;

			if (string.IsNullOrEmpty(Password))
				Flags  &= ~EnterFlags.UsePassword;
			else Flags |= EnterFlags.UsePassword;

			buffer.Write(Flags);

			if (Flags.HasFlag(EnterFlags.UsePseudonyme))
				buffer.Write(Display);

			if (Flags.HasFlag(EnterFlags.UsePassword))
				buffer.Write(Password);

			return buffer;
		}
	}
}