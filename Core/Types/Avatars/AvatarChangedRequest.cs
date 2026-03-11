using Nox.Avatars;
using Nox.Relay.Core.Types.Contents.Rooms;
using Buffer = Nox.CCK.Utils.Buffer;

namespace Nox.Relay.Core.Types.Avatars {
	/// <summary>
	/// Request to change the avatar of a player in a room.
	/// </summary>
	/// <remarks>
	/// Wire format (after the iid byte prepended by Room.Emit):
	/// [pid: u16][avatar_id: u32][server: string][version: u16]
	/// </remarks>
	public class AvatarChangedRequest : RoomRequest {
		/// <summary>
		/// The ID of the player whose avatar should be changed.
		/// Use <see cref="ushort.MaxValue"/> to change the local player's own avatar.
		/// </summary>
		public ushort PlayerId = ushort.MaxValue;

		/// <summary>
		/// The identifier of the new avatar to apply.
		/// </summary>
		public IAvatarIdentifier AvatarIdentifier;

		/// <summary>
		/// Creates a request to change the local player's own avatar.
		/// </summary>
		/// <param name="avatar">The new avatar identifier.</param>
		/// <returns></returns>
		public static AvatarChangedRequest Self(IAvatarIdentifier avatar)
			=> new() {
				PlayerId         = ushort.MaxValue,
				AvatarIdentifier = avatar
			};

		/// <summary>
		/// Creates a request to change another player's avatar (requires privilege).
		/// </summary>
		/// <param name="playerId">The target player's ID.</param>
		/// <param name="avatar">The new avatar identifier.</param>
		/// <returns></returns>
		public static AvatarChangedRequest For(ushort playerId, IAvatarIdentifier avatar)
			=> new() {
				PlayerId         = playerId,
				AvatarIdentifier = avatar
			};

		public override Buffer ToBuffer() {
			var buffer = new Buffer();

			// pid — ushort.MaxValue means "self" on the server side
			buffer.Write(PlayerId);

			// avatar_id, server, version
			buffer.Write(AvatarIdentifier?.GetId() ?? 0u);
			buffer.Write(AvatarIdentifier?.GetServer() ?? string.Empty);
			buffer.Write(AvatarIdentifier?.GetVersion() ?? (ushort)0);

			return buffer;
		}
	}
}
