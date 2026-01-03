using Nox.CCK.Utils;
using Nox.Relay.Core.Types.Contents.Rooms;

namespace Nox.Relay.Core.Types.Traveling {
    /// <summary>
    /// Request to perform a traveling action within a room.
    /// </summary>
    public class TravelingRequest : RoomRequest {
        /// <summary>
        /// The traveling action to perform.
        /// </summary>
        public TravelingAction Action;

        /// <summary>
        /// Optional reason for failure if the action is <see cref="TravelingAction.Failed"/>.
        /// </summary>
        public string Reason;

        public static TravelingRequest Failed(string reason)
            => new TravelingRequest {
                Action = TravelingAction.Failed,
                Reason = reason
            };

        public static TravelingRequest Ready()
            => new TravelingRequest {
                Action = TravelingAction.Ready
            };

        public static TravelingRequest Travel()
            => new TravelingRequest {
                Action = TravelingAction.Travel
            };

        public override Buffer ToBuffer() {
            var buffer = new Buffer();
            buffer.Write(Action);
            if (Action == TravelingAction.Failed && !string.IsNullOrEmpty(Reason))
                buffer.Write(Reason);
            return buffer;
        }
    }
}