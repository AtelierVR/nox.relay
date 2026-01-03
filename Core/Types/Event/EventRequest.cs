using System;
using System.Collections.Generic;
using System.Linq;
using Nox.CCK.Network;
using Nox.Relay.Core.Types.Contents.Rooms;
using Buffer = Nox.CCK.Utils.Buffer;

namespace Nox.Relay.Core.Types.Event {
	public class EventRequest : RoomRequest {
		/// <summary>
		/// Default maximum size of the event payload in bytes.
		/// </summary>
		public const int MaxPayloadSize = 1024;

		/// <summary>
		/// Default maximum number of target IDs for a targeted event.
		/// </summary>
		public const int MaxTargetCount = 255;

		/// <summary>
		/// Name of the event.
		/// Is commonly a <see cref="Nox.CCK.Network.Serializer.Hash"/> of a string.
		/// </summary>
		public long Name;

		/// <summary>
		/// Payload of the event.
		/// </summary>
		public byte[] Payload = Array.Empty<byte>();

		/// <summary>
		/// Target IDs of players to receive the event.
		/// If null or empty, the event is broadcast to all players in the room.
		/// </summary>
		public ushort[] TargetIds = Array.Empty<ushort>();

		/// <summary>
		/// Creates a broadcast event request.
		/// </summary>
		/// <param name="name"></param>
		/// <param name="payload"></param>
		/// <returns></returns>
		/// <exception cref="ArgumentException">If the payload size exceeds <see cref="MaxPayloadSize"/>.</exception>
		public static EventRequest Broadcast(string name, byte[] payload)
			=> Broadcast(name.Hash(), payload);

		/// <summary>
		/// Creates a broadcast event request.
		/// With the event name as a <see cref="long"/> hash.
		/// </summary>
		/// <param name="name"></param>
		/// <param name="payload"></param>
		/// <returns></returns>
		/// <exception cref="ArgumentException">If the payload size exceeds <see cref="MaxPayloadSize"/>.</exception>
		public static EventRequest Broadcast(long name, byte[] payload) {
			if (payload.Length > MaxPayloadSize)
				throw new ArgumentException($"Payload size cannot exceed {MaxPayloadSize} bytes.", nameof(payload));
			return new EventRequest {
				Name      = name,
				Payload   = payload,
				TargetIds = Array.Empty<ushort>()
			};
		}

		/// <summary>
		/// Creates a targeted event request.
		/// </summary>
		/// <param name="name"></param>
		/// <param name="payload"></param>
		/// <param name="targetIds"></param>
		/// <returns></returns>
		/// <exception cref="ArgumentException">If the payload size exceeds <see cref="MaxPayloadSize"/> or if the number of target IDs exceeds <see cref="MaxTargetCount"/> or if no target IDs are provided.</exception>
		public static EventRequest CreateTargeted(string name, byte[] payload, ushort[] targetIds)
			=> CreateTargeted(name.Hash(), payload, targetIds);

		/// <summary>
		/// Creates a targeted event request.
		/// With the event name as a <see cref="long"/> hash.
		/// </summary>
		/// <param name="name"></param>
		/// <param name="payload"></param>
		/// <param name="targetIds"></param>
		/// <returns></returns>
		/// <exception cref="ArgumentException">If the payload size exceeds <see cref="MaxPayloadSize"/> or if the number of target IDs exceeds <see cref="MaxTargetCount"/> or if no target IDs are provided.</exception>
		public static EventRequest CreateTargeted(long name, byte[] payload, ushort[] targetIds) {
			if (payload.Length > MaxPayloadSize)
				throw new ArgumentException($"Payload size cannot exceed {MaxPayloadSize} bytes.", nameof(payload));
			targetIds = new HashSet<ushort>(targetIds).ToArray();
			return targetIds.Length switch {
				0                => throw new ArgumentException("Target IDs cannot be empty.", nameof(targetIds)),
				> MaxTargetCount => throw new ArgumentException($"Target IDs cannot exceed {MaxTargetCount} bytes (got {targetIds.Length} bytes)", nameof(targetIds)),
				_                => new EventRequest { Name = name, Payload = payload, TargetIds = targetIds }
			};
		}

		public override Buffer ToBuffer() {
			var buffer = new Buffer();
			buffer.Write(Name);
			var payload = Payload ?? Array.Empty<byte>();
			buffer.Write((ushort)payload.Length);
			buffer.Write(payload);

			if (TargetIds == null || TargetIds.Length == 0)
				return buffer;

			buffer.Write((byte)TargetIds.Length);
			foreach (var targetId in TargetIds)
				buffer.Write(targetId);

			return buffer;
		}

		public override string ToString()
			=> $"{GetType().Name}[InternalId={Room.InternalId}, Name={Name}, PayloadLength={Payload?.Length ?? 0}, TargetCount={(TargetIds == null ? "Broadcast" : TargetIds.Length.ToString())}]";
	}
}