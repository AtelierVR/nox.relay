using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Nox.CCK.Utils;
using Nox.Relay.Core.Players;
using Nox.Relay.Core.Types;
using Nox.Relay.Core.Types.Rooms;
using Buffer = Nox.CCK.Utils.Buffer;

namespace Nox.Relay.Core.Rooms {
	/// <summary>
	/// Represents a relay room with its associated properties.
	/// A room is hosted on a specific relay and can accommodate multiple players.
	/// </summary>
	public class Room {
		/// <summary>
		/// Unique identifier to the node hosting this room.
		/// The address is defined in the <see cref="Nox.Relay.Core.Types.Handshakes.HandshakeResponse"/> structure.
		/// </summary>
		public uint NodeId;

		/// <summary>
		/// Is the unique id of the room assigned internally by the relay server.
		/// This ID is used to reference for requests and responses related to this room.
		/// </summary>
		public byte InternalId;

		/// <summary>
		/// Current number of players connected to this room.
		/// Used to determine if the room is full or has available slots for new players.
		/// </summary>
		public ushort PlayerCount;

		/// <summary>
		/// Maximum number of players that can join this room.
		/// <see cref="RoomFlags.AllowOverload"/> can allow more players than this value.
		/// </summary>
		public ushort MaxPlayerCount;

		/// <summary>
		/// Flags representing various properties and behaviors of the room.
		/// These flags can indicate if the room is private,
		/// allows overload, or has other special conditions.
		/// </summary>
		public RoomFlags Flags;

		/// <summary>
		/// Current ticks per second (TPS) rate of the room.
		/// This value indicates the performance the recurrent update rate of the room.
		/// A lower TPS can indicate a laggy or overloaded room.
		/// </summary>
		public byte Tps = byte.MaxValue;

		/// <summary>
		/// The relay connection associated with this room.
		/// This connection is used to send and receive data related to this room.
		/// </summary>
		public Relay Connection;

		/// <summary>
		/// Handles the reception of data for this relay room.
		/// Is called by <see cref="Relay.HandleInstance"/>, when data is received for this room.
		/// </summary>
		/// <param name="length"></param>
		/// <param name="state"></param>
		/// <param name="type"></param>
		/// <param name="buffer"></param>
		internal async UniTask OnReceived(ushort length, ushort state, ResponseType type, Buffer buffer) {
			buffer.Start();

			// ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
			switch (type) {
				case ResponseType.Quit:
					HandleQuit(length, state, type, buffer);
					break;
				case ResponseType.Enter:
					HandleEnter(length, state, type, buffer);
					break;
				case ResponseType.Join:
					HandleJoin(length, state, type, buffer);
					break;
				case ResponseType.Leave:
					HandleLeave(length, state, type, buffer);
					break;
				default:
					Logger.LogWarning($"[] Unhandled room response type: {type} (length: {length}, state: {state})");
					break;
			}
		}

		private string Tag
			=> $"Room:{InternalId}";

		private void HandleQuit(ushort length, ushort state, ResponseType type, Buffer buffer) {
			var quit = new Types.Quit.QuitEvent { State = state, Connection = Connection, Room = this };
			if (!quit.FromBuffer(buffer)) {
				Logger.LogWarning("Failed to parse QuitEvent", tag: Tag);
				return;
			}

			Players.Clear();
			Connection.Rooms.Remove(this);
		}

		private void HandleEnter(ushort length, ushort state, ResponseType type, Buffer buffer) {
			var enter = new Types.Enter.EnterResponse { State = state, Connection = Connection, Room = this };
			if (!enter.FromBuffer(buffer)) {
				Logger.LogWarning("Failed to parse EnterResponse", tag: Tag);
				return;
			}

			if (enter.IsError)
				return;

			Players.Add(enter.Player);
			Connection.Rooms.Add(this);
		}

		private void HandleJoin(ushort length, ushort state, ResponseType type, Buffer buffer) {
			var join = new Types.Join.JoinEvent { State = state, Connection = Connection, Room = this };
			if (!join.FromBuffer(buffer)) {
				Logger.LogWarning("Failed to parse JoinEvent", tag: Tag);
				return;
			}

			Players.Add(join.Player);
		}

		private void HandleLeave(ushort length, ushort state, ResponseType type, Buffer buffer) {
			var leave = new Types.Leave.LeaveEvent { State = state, Connection = Connection, Room = this };
			if (!leave.FromBuffer(buffer)) {
				Logger.LogWarning("Failed to parse LeaveEvent", tag: Tag);
				return;
			}

			Players.RemoveWhere(p => p.Id == leave.PlayerId);
		}

		public readonly HashSet<Player> Players = new();


		public async UniTask<Types.Enter.EnterResponse> Enter(Types.Enter.EnterRequest request) {
			request.InternalId = InternalId;

			var enter = await Connection.Request<Types.Enter.EnterResponse>(
				request,
				RequestType.Enter,
				ResponseType.Enter,
				Connection.NextState()
			);

			enter      ??= Types.Enter.EnterResponse.Unknown(this, "Unknown enter request");
			enter.Room =   this;

			if (enter.IsError)
				return enter;

			Players.Add(enter.Player);
			Connection.Rooms.Add(this);

			return enter;
		}

		public async UniTask<Types.Quit.QuitEvent> Quit(Types.Quit.QuitType type = Types.Quit.QuitType.Normal, string reason = null) {
			var quit = await Connection.Request<Types.Quit.QuitEvent>(
				new Types.Quit.QuitRequest {
					InternalId = InternalId,
					Type       = type,
					Reason     = reason
				},
				RequestType.Quit,
				ResponseType.Quit,
				Connection.NextState()
			);

			quit      ??= Types.Quit.QuitEvent.Unknown(this, "Unknown quit request");
			quit.Room =   this;

			Players.Clear();
			Connection.Rooms.Remove(this);

			return quit;
		}
	}
}