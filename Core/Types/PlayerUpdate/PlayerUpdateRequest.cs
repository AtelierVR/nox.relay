using Nox.CCK.Utils;
using Nox.Relay.Core.Types.Contents.Rooms;
using Buffer = Nox.CCK.Utils.Buffer;

namespace Nox.Relay.Core.Types.PlayerUpdate {
	/// <summary>
	/// Request to update a player's mutable properties inside an instance.
	/// <para>
	/// Send with <c>flags = 0</c> (and no other fields) to ask the server to echo back
	/// the current state of the target player.
	/// </para>
	/// Wire format (after the outer <c>[iid:u8]</c> prefix added by <c>Room.Emit</c>):
	/// <code>[player_id:u16][flags:u8][display_name:string?]</code>
	/// </summary>
	public class PlayerUpdateRequest : RoomRequest {
		/// <summary>
		/// The player to update.
		/// Use <c>ushort.MaxValue</c> or your own player ID to update yourself.
		/// </summary>
		public ushort PlayerId = ushort.MaxValue;

		/// <summary>Which fields to update. Set to <see cref="PlayerUpdateFlags.None"/> to request a read-back.</summary>
		public PlayerUpdateFlags Flags = PlayerUpdateFlags.None;

		/// <summary>New display name. Required when <see cref="Flags"/> includes <see cref="PlayerUpdateFlags.DisplayName"/>.</summary>
		public string DisplayName;

		public override Buffer ToBuffer() {
			var buffer = new Buffer();
			buffer.Write(PlayerId);
			buffer.Write((byte)Flags);
			if (Flags.HasFlag(PlayerUpdateFlags.DisplayName))
				buffer.Write(DisplayName ?? string.Empty);
			return buffer;
		}
	}
}
