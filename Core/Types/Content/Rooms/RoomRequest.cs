using Nox.Relay.Core.Rooms;

namespace Nox.Relay.Core.Types.Contents.Rooms {
	/// <summary>
	/// Base class for all room-related requests in the relay system.
	/// </summary>
	public abstract class RoomRequest : ContentRequest {
		/// <summary>
		/// The room data associated with this request.
		/// </summary>
		public Room Room;
	}
}