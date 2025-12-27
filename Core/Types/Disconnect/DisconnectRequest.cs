using Nox.CCK.Utils;
using Nox.Relay.Core.Types.Contents;

namespace Nox.Relay.Core.Types.Disconnect {
	/// <summary>
	/// Request to disconnect from the relay server.
	/// </summary>
	public class DisconnectRequest : ContentRequest {
		/// <summary>
		/// Reason for the disconnection.
		/// Can be empty.
		/// </summary>
		public string Reason;

		public override Buffer ToBuffer() {
			var buffer = new Buffer();
			if (!string.IsNullOrEmpty(Reason))
				buffer.Write(Reason);
			return buffer;
		}
	}
}