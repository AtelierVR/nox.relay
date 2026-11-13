using System;
using Nox.CCK.Utils;
using Buffer = Nox.CCK.Utils.Buffer;

namespace Nox.Relay.Core.Connectors {
	/// <summary>
	/// A packet as it came out of the transport: the payload plus the instant the transport read it.
	/// <para>
	/// The stamp is taken on the network thread, <i>before</i> the packet is handed over to the main
	/// thread, so timing anything with it does not include the frame the dispatch costs (several
	/// milliseconds at high frame rates). That is what makes a local round-trip measurable at all.
	/// </para>
	/// </summary>
	public readonly struct ReceivedPacket {
		public readonly Buffer Buffer;

		/// <summary>Wall clock instant of the read.</summary>
		public readonly DateTime At;

		public ReceivedPacket(Buffer buffer, DateTime at) {
			Buffer = buffer;
			At     = at;
		}

		/// <summary>
		/// Stamps a freshly read packet. Must be called from the thread that received it.
		/// </summary>
		public static ReceivedPacket Now(Buffer buffer)
			=> new(buffer, Clock.UtcNow);
	}
}
