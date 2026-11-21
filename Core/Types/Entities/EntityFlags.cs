using System;

namespace Nox.Relay.Core.Types.Entities {
	/// <summary>
	/// Behaviour flags of a networked entity, carried by an <see cref="EntityRequest"/> and echoed by
	/// the relay in the <see cref="EntityEvent"/>.
	/// </summary>
	[Flags]
	public enum EntityFlags : byte {
		None = 0,
	}
}
