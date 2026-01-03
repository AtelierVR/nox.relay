using Nox.CCK.Utils;
using Nox.Relay.Core.Types.Contents.Rooms;

namespace Nox.Relay.Core.Types.Quit {
	/// <summary>
	/// Request to quit a room with a specified type and optional reason.
	/// </summary>
	public class QuitRequest : RoomRequest {
		/// <summary>
		/// The type of quit action to perform.
		/// </summary>
		public QuitType Type;

		/// <summary>
		/// Optional reason for quitting the room.
		/// </summary>
		public string Reason;

		public override Buffer ToBuffer() {
			var buffer = new Buffer();
			
			buffer.Write(Type);
			if (!string.IsNullOrEmpty(Reason))
				buffer.Write(Reason);
			
			return buffer;
		}
	}
}