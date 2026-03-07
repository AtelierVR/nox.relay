using Nox.CCK.Utils;
using Nox.Relay.Core.Players;
using Nox.Relay.Core.Types.Content.Rooms;
using Buffer = Nox.CCK.Utils.Buffer;

namespace Nox.Relay.Core.Types.PlayerUpdate {
	/// <summary>
	/// Response to a <see cref="PlayerUpdateRequest"/>, and also used for server-initiated
	/// player-update broadcast packets (<see cref="PlayerUpdateResult.Change"/>).
	/// <para>
	/// Wire format (after outer <c>[iid:u8]</c> is stripped):
	/// <code>[result:u8][player_id:u16][flags:u8][display_name:string?][player_flags:u32?]</code>
	/// </para>
	/// </summary>
	public class PlayerUpdateResponse : RoomResponse {
		/// <summary>Outcome of the update operation, or <see cref="PlayerUpdateResult.Change"/> for a broadcast.</summary>
		public PlayerUpdateResult Result;

		/// <summary>The player whose data was updated.</summary>
		public ushort PlayerId;

		/// <summary>Which fields are present in this packet.</summary>
		public PlayerUpdateFlags Flags;

		/// <summary>Updated display name. Present when <see cref="Flags"/> includes <see cref="PlayerUpdateFlags.DisplayName"/>.</summary>
		public string DisplayName;

		/// <summary>Updated player flags bitmask. Present when <see cref="Flags"/> includes <see cref="PlayerUpdateFlags.Flags"/>.</summary>
		public uint PlayerFlags;

		/// <summary><c>true</c> when the operation was rejected by the server.</summary>
		public bool IsError => Result == PlayerUpdateResult.Failure;

		public override bool FromBuffer(Buffer buffer) {
			buffer.Start();

			Result   = buffer.ReadEnum<PlayerUpdateResult>();
			PlayerId = buffer.ReadUShort();
			Flags    = buffer.ReadEnum<PlayerUpdateFlags>();

			if (Flags.HasFlag(PlayerUpdateFlags.DisplayName))
				DisplayName = buffer.ReadString();
			if (Flags.HasFlag(PlayerUpdateFlags.Flags))
				PlayerFlags = buffer.ReadUInt();

			return true;
		}

		public override string ToString()
			=> $"{GetType().Name}[Result={Result}, PlayerId={PlayerId}, Flags={Flags}]";
	}
}
