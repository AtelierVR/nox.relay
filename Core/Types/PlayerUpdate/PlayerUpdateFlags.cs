using System;

namespace Nox.Relay.Core.Types.PlayerUpdate {
	/// <summary>
	/// Bit-flags indicating which fields are included in a <see cref="PlayerUpdateRequest"/>
	/// or <see cref="PlayerUpdateResponse"/>.
	/// </summary>
	[Flags]
	public enum PlayerUpdateFlags : byte {
		/// <summary>No fields updated.</summary>
		None = 0x00,

		/// <summary>The player's display name is included.</summary>
		DisplayName = 0x01,

		/// <summary>The player's flag bitmask is included.</summary>
		Flags = 0x02,

		/// <summary>All fields are included.</summary>
		All = DisplayName | Flags,
	}
}
