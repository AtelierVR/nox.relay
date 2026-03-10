namespace Nox.Relay.Core.Connectors {
	/// <summary>
	/// Specifies the channel to use when sending a packet.
	/// </summary>
	public enum SendType : byte {
		/// <summary>
		/// Automatically select the channel based on the protocol used.
		/// </summary>
		Auto = 0,

		/// <summary>
		/// Send on a new bi-directional stream (reliable, ordered, request/response).
		/// </summary>
		Stream = 1,

		/// <summary>
		/// Send as a datagram (unreliable, unordered, low-latency).
		/// </summary>
		Datagram = 2,
	}
}
