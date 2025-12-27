using Nox.Relay.Core.Rooms;
using Nox.Relay.Core.Types.Contents;

namespace Nox.Relay.Core.Types.Content.Rooms {
	/// <summary>
	/// Base class for all room-related responses in the relay system.
	/// </summary>
	public abstract class RoomResponse : ContentResponse {
		/// <summary>
		/// The room data associated with this response.
		/// </summary>
		public Room Room;
	}
}