namespace Nox.Relay.Core.Types.Contents.Rooms {
	/// <summary>
	/// Base class for all room-related requests in the relay system.
	/// </summary>
	public abstract class RoomRequest : ContentRequest {
		/// <summary>
		/// Room identifier used to target specific rooms.
		/// </summary>
		public byte InternalId;
	}
}