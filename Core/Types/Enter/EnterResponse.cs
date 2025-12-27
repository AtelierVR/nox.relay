using System;
using Nox.CCK.Users;
using Nox.Relay.Core.Players;
using Nox.Relay.Core.Rooms;
using Nox.Relay.Core.Types.Content.Rooms;
using Buffer = Nox.CCK.Utils.Buffer;

namespace Nox.Relay.Core.Types.Enter {
	/// <summary>
	/// Response received when attempting to enter a room.
	/// </summary>
	public class EnterResponse : RoomResponse {
		/// <summary>
		/// The result of the enter attempt.
		/// </summary>
		public EnterResult Result;

		/// <summary>
		/// If the player is blacklisted, the date and time when the ban expires.
		/// If the value is epoches, the blacklist is permanent.
		/// </summary>
		public DateTime ExpireAt = DateTime.MinValue;

		/// <summary>
		/// The reason for failure, if applicable.
		/// It can be null/empty.
		/// </summary>
		public string Reason;

		/// <summary>
		/// The player information upon successful entry.
		/// </summary>
		public Player Player;

		/// <summary>
		/// The maximum ticks per second (TPS) allowed in the room.
		/// </summary>
		public byte Tps;

		/// <summary>
		/// The threshold value for the room.
		/// Is used to limit certain actions or behaviors,
		/// like transforming precision or network quality.
		/// </summary>
		public float Threshold;

		/// <summary>
		/// THe maximum render distance for entities in the room before they are not rendered anymore.
		/// </summary>
		public float RenderEntity;

		/// <summary>
		/// Indicates whether the blacklist has an expiration time.
		/// </summary>
		public bool HasExpiration
			=> ExpireAt != DateTime.MinValue;

		/// <summary>
		/// Indicates whether the enter response represents an error.
		/// </summary>
		public bool IsError
			=> Result is not EnterResult.Success;


		/// <summary>
		/// Creates an unknown enter response for the specified room with the given reason.
		/// </summary>
		/// <param name="room"></param>
		/// <param name="reason"></param>
		/// <returns></returns>
		public static EnterResponse Unknown(Room room, string reason)
			=> new() {
				Connection = room.Connection,
				Room       = room,
				Result     = EnterResult.Unknown,
				Reason     = reason
			};

		public override bool FromBuffer(Buffer buffer) {
			Result = buffer.ReadEnum<EnterResult>();
			switch (Result) {
				case EnterResult.NotFound:
				case EnterResult.Full:
				case EnterResult.NotWhitelisted:
				case EnterResult.InvalidGame:
				case EnterResult.IncorrectPassword:
				case EnterResult.Refused:
				case EnterResult.InvalidPseudonyme:
				case EnterResult.Unknown:
					Reason = buffer.Remaining >= 2 ? buffer.ReadString() : "Unknown error";
					return true;
				case EnterResult.Blacklisted:
					ExpireAt = buffer.ReadDateTime();
					Reason   = buffer.ReadString();
					return true;
				case EnterResult.Success:
					Player = new Player {
						Room       = Room,
						Flags      = buffer.ReadEnum<PlayerFlags>(),
						Id         = buffer.ReadUShort(),
						Identifier = new UserIdentifier(buffer.ReadUInt(), buffer.ReadString()),
						Display    = buffer.ReadString(),
						JoinedAt   = buffer.ReadDateTime(),
					};
					Tps          = buffer.ReadByte();
					Threshold    = buffer.ReadFloat();
					RenderEntity = buffer.ReadFloat();
					return true;
				default:
					return false;
			}
		}

		public override string ToString()
			=> $"{GetType().Name}[Result={Result}, Player={Player?.ToString() ?? "null"}, MaxTps={Tps}]";
	}
}