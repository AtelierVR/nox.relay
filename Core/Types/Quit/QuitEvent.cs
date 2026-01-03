using Nox.CCK.Utils;
using Nox.Relay.Core.Rooms;
using Nox.Relay.Core.Types.Content.Rooms;

namespace Nox.Relay.Core.Types.Quit {
    /// <summary>
    /// Represents a quit event in a room,
    /// indicating that a user has left and why.
    /// </summary>
    public class QuitEvent : RoomResponse {
        /// <summary>
        /// The type of quit event.
        /// </summary>
        public QuitType Type;

        /// <summary>
        /// The reason for the quit event, if provided.
        /// </summary>
        public string Reason;

        public override bool FromBuffer(Buffer buffer) {
            buffer.Start();
            
            Type = buffer.ReadEnum<QuitType>();
            if (buffer.Remaining > 2) // Check if there is reason data
                Reason = buffer.ReadString();
            return true;
        }

        public static QuitEvent Unknown(Room room, string reason)
            => new() {
                Connection = room.Connection,
                Room = room,
                Type = QuitType.UnknownError,
                Reason = reason
            };

        public override string ToString()
            => $"{GetType().Name}[Iid={Room.InternalId}, Type={Type}, Reason={Reason ?? "null"}]";
    }
}