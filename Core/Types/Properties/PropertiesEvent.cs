using System.Collections.Generic;
using Nox.Relay.Core.Types.Content.Rooms;
using Buffer = Nox.CCK.Utils.Buffer;

namespace Nox.Relay.Core.Types.Properties {
	/// <summary>
	/// Represents a properties event sent within a room.
	/// Properties events are used to set or clear properties on entities within the room.
	/// </summary>
	public class PropertiesEvent : RoomResponse {
		/// <summary>
		/// Id of <see cref="Nox.Relay.Core.Players.Player"/> who sent the properties event.
		/// </summary>
		public uint SenderId;

		/// <summary>
		/// The ID of the entity whose properties are being modified.
		/// </summary>
		public uint EntityId;

		/// <summary>
		/// The parameters to set or clear, where the key is the property key
		/// and the value is the serialized property data.
		/// An empty byte array indicates that the property should be cleared.
		/// An empty properties dictionary indicates that all properties should be cleared.
		/// </summary>
		public readonly Dictionary<int, byte[]> Properties = new();

		public override bool FromBuffer(Buffer buffer) {
			buffer.Start();

			EntityId = buffer.ReadUInt();
			SenderId = buffer.ReadUInt();
			var parameterCount = buffer.ReadByte();

			for (var i = 0; i < parameterCount; i++) {
				var parameterId = buffer.ReadInt();
				var payloadSize = buffer.ReadByte();
				var payload     = buffer.ReadBytes(payloadSize);
				Properties[parameterId] = payload;
			}

			return true;
		}

		public override string ToString()
			=> $"{GetType().Name}[InternalId={Room.InternalId}, EntityId={EntityId}, ParametersCount={Properties.Count}, SenderId={SenderId}]";
	}
}