using System;

namespace Nox.Relay.Core.Types.ServerConfig {
	/// <summary>
	/// Bit-flags selecting which server configuration fields are included in a
	/// <see cref="ServerConfigRequest"/> or <see cref="ServerConfigResponse"/>.
	/// </summary>
	[Flags]
	public enum ServerConfigFlags : byte {
		/// <summary>No fields selected (used to echo the current config back).</summary>
		None = 0x00,

		/// <summary>Ticks-per-second value is included.</summary>
		TPS = 0x01,

		/// <summary>Transform update threshold is included.</summary>
		Threshold = 0x02,

		/// <summary>Maximum player capacity is included.</summary>
		Capacity = 0x04,

		/// <summary>Password (set / clear) is included.</summary>
		Password = 0x08,

		/// <summary>Instance flag bitmask is included.</summary>
		Flags = 0x10,

		/// <summary>Minimum TPS value is included.</summary>
		MinTPS = 0x20,

		/// <summary>Maximum TPS value is included.</summary>
		MaxTPS = 0x40,

		/// <summary>Load-balancing enabled flag is included.</summary>
		LoadBalancing = 0x80,

		/// <summary>All fields are included.</summary>
		All = 0xFF,
	}
}
