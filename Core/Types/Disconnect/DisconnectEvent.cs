using Nox.CCK.Utils;
using Nox.Relay.Core.Types.Contents;

namespace Nox.Relay.Core.Types.Disconnect {
	/// <summary>
	/// Disconnect event sent by the server to the client
	/// </summary>
	public class DisconnectEvent : ContentResponse {
		/// <summary>
		/// Reason for the disconnection
		/// Can be empty.
		/// </summary>
		public string Reason;

		public override bool FromBuffer(Buffer buffer) {
			buffer.Seek(0);
			Reason = buffer.Remaining > 0
				? buffer.ReadString()
				: string.Empty;
			return true;
		}
	}
}