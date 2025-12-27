namespace Nox.Relay.Core.Types {
	/// <summary>
	/// Types of responses of the server.
	/// </summary>
	public enum ResponseType : byte {
		None = 0xFF,

		// System Messages
		Disconnect   = 0x00,
		Handshake    = 0x01,
		Segmentation = 0x02,
		Reliable     = 0x03,
		Latency      = 0x04,

		Authentification    = 0x05,
		Enter               = 0x06,
		Quit                = 0x07,
		Custom              = 0x08,
		PasswordRequirement = 0x09,
		Traveling           = 0x0A,
		Transform           = 0x0B,
		Teleport            = 0x0C,
		AvatarChanged       = 0x0D,
		ServerConfig        = 0x0E,
		Properties          = 0x0F,
		Join                = 0x10,
		Leave               = 0x11,
		PlayerUpdate        = 0x12,
		Rooms               = 0x13,
		Voice               = 0x14,
		Event               = 0x15,
	}
}