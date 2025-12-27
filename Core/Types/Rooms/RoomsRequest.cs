using Nox.CCK.Utils;
using Nox.Relay.Core.Types.Contents;

namespace Nox.Relay.Core.Types.Rooms {
	/// <summary>
	/// Request to retrieve a list of rooms, with pagination support.
	/// </summary>
	public class RoomsRequest : ContentRequest {
		/// <summary>
		/// The page number to retrieve.
		/// Each page contains a non-predefined number of rooms.
		/// </summary>
		public byte Page;

		public override Buffer ToBuffer() {
			var buffer = new Buffer();
			buffer.Write(Page);
			return buffer;
		}
	}
}