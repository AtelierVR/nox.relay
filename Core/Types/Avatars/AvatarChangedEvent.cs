using System.Collections.Generic;
using Nox.Avatars;
using Nox.CCK.Avatars;
using Nox.CCK.Utils;
using Nox.Relay.Core.Types.Content.Rooms;

namespace Nox.Relay.Core.Types.Avatars {
	/// <summary>
	/// Event representing a change in a player's avatar within a room.
	/// </summary>
	public class AvatarChangedEvent : RoomResponse {
		/// <summary>
		/// The result of the avatar change operation.
		/// </summary>
		public AvatarChangedResult Result;

		/// <summary>
		/// The reason for failure, if applicable.
		/// </summary>
		public string Reason;

		/// <summary>
		/// The ID of the player whose avatar is changing.
		/// </summary>
		public ushort PlayerId;

		/// <summary>
		/// The identifier of the new avatar.
		/// </summary>
		public IAvatarIdentifier AvatarIdentifier;

		/// <summary>
		/// Indicates whether the avatar change resulted in an error.
		/// </summary>
		public bool IsError
			=> Result is AvatarChangedResult.Failed or AvatarChangedResult.Unknown;

		/// <summary>
		/// Indicates whether the avatar change was successful.
		/// </summary>
		public bool IsSuccess
			=> Result == AvatarChangedResult.Success;

		public override bool FromBuffer(Buffer buffer) {
			buffer.Start();

			Result = buffer.ReadEnum<AvatarChangedResult>();

			switch (Result) {
				case AvatarChangedResult.Success:
					break;
				case AvatarChangedResult.Changing: {
					PlayerId = buffer.ReadUShort();
					var id = buffer.ReadUInt();
					var server = buffer.ReadString();
					var version = buffer.ReadUShort();
					var meta = new Dictionary<string, string[]> { { "v", new[] { version.ToString() } } };
					AvatarIdentifier = new AvatarIdentifier(id, meta, server);
					break;
				}
				case AvatarChangedResult.Unknown:
				case AvatarChangedResult.Failed:
				default:
					if (buffer.Remaining > 2)
						Reason = buffer.ReadString();
					break;
			}

			return true;
		}

		public override string ToString()
			=> $"{GetType().Name}[InternalId={Room.InternalId}, Result={Result}"
				+ (IsError ? $", Reason={Reason}" : "")
				+ (Result == AvatarChangedResult.Changing ? $", PlayerId={PlayerId}, AvatarIdentifier={AvatarIdentifier?.ToString()}" : "")
				+ "]";
	}
}