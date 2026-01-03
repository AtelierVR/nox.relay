using System;
using System.Collections.Generic;
using System.Linq;
using Nox.Entities;
using Nox.Relay.Core.Types.Contents.Rooms;
using Buffer = Nox.CCK.Utils.Buffer;

namespace Nox.Relay.Core.Types.Properties {
	/// <summary>
	/// Request to set or clear properties for a specific entity in a room.
	/// </summary>
	public class PropertiesRequest : RoomRequest {
		/// <summary>
		/// Default maximum number of parameters in a property request.
		/// </summary>
		public const int MaxParameters = byte.MaxValue;

		/// <summary>
		/// The ID of the entity whose properties are being modified.
		/// If the Id is <see cref="ushort.MaxValue"/>, the properties apply to self player.
		/// </summary>
		public ushort EntityId;

		/// <summary>
		/// The parameters to set or clear, where the key is the property key
		/// and the value is the serialized property data.
		/// An empty byte array indicates that the property should be cleared.
		/// An empty properties dictionary indicates that all properties should be cleared.
		/// </summary>
		public Dictionary<int, byte[]> Parameters = new();

		/// <summary>
		/// Creates a <see cref="PropertiesRequest"/> to set properties for a specific entity.
		/// </summary>
		/// <param name="playerId"></param>
		/// <param name="parameters"></param>
		/// <returns></returns>
		/// <exception cref="ArgumentException"></exception>
		public static PropertiesRequest Create(ushort playerId, IProperty[] parameters) {
			if (parameters.Length > MaxParameters)
				throw new ArgumentException($"Cannot have more than {MaxParameters} parameters in an property request.", nameof(parameters));
			return new PropertiesRequest {
				EntityId   = playerId,
				Parameters = parameters.ToDictionary(p => p.Key, p => p.Serialize())
			};
		}

		/// <summary>
		/// Creates a <see cref="PropertiesRequest"/> to clear all properties for a specific entity.
		/// </summary>
		/// <param name="playerId"></param>
		/// <returns></returns>
		public static PropertiesRequest ClearAll(ushort playerId)
			=> new() {
				EntityId   = playerId,
				Parameters = new Dictionary<int, byte[]>()
			};

		/// <summary>
		/// Creates a <see cref="PropertiesRequest"/> to clear specific properties for a specific entity.
		/// </summary>
		/// <param name="playerId"></param>
		/// <param name="parameters"></param>
		/// <returns></returns>
		/// <exception cref="ArgumentException"></exception>
		public static PropertiesRequest Clear(ushort playerId, IProperty[] parameters) {
			if (parameters.Length > MaxParameters)
				throw new ArgumentException($"Cannot have more than {MaxParameters} parameters in an property clear request.", nameof(parameters));
			return new PropertiesRequest {
				EntityId   = playerId,
				Parameters = parameters.ToDictionary(p => p.Key, _ => Array.Empty<byte>())
			};
		}

		public override Buffer ToBuffer() {
			var buffer = new Buffer();
			
			buffer.Write(EntityId);
			buffer.Write((byte)Parameters.Count);

			foreach (var parameter in Parameters) {
				buffer.Write(parameter.Key);
				buffer.Write((byte)parameter.Value.Length);
				buffer.Write(parameter.Value);
			}

			return buffer;
		}

		public override string ToString()
			=> $"{GetType().Name}[InternalId={Room.InternalId}, EntityId={EntityId}, ParameterCount={Parameters.Count}]";
	}
}