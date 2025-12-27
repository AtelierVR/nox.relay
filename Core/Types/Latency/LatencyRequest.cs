using System;
using Buffer = Nox.CCK.Utils.Buffer;
using Nox.Relay.Core.Types.Contents;

namespace Nox.Relay.Core.Types.Latency {
	/// <summary>
	/// Represents a latency measurement request sent to a relay server.
	/// Is used to keep-alive connections and measure round-trip time.
	/// The client is the only one to keep-alive.
	/// Is sent periodically (with <see cref="Handshakes.HandshakeResponse.KeepAliveInterval"/> as interval).
	/// </summary>
	public class LatencyRequest : ContentRequest {
		/// <summary>
		/// The timestamp marking when the latency request was initiated.
		/// </summary>
		public DateTime InitialTime;

		/// <summary>
		/// A unique challenge number used to correlate requests and responses.
		/// </summary>
		public long Challenge;

		public override Buffer ToBuffer() {
			var buffer = new Buffer();
			buffer.Write(InitialTime);
			buffer.Write(Challenge);
			return buffer;
		}

		/// <summary>
		/// Creates a new <see cref="LatencyRequest"/> with the current UTC time and a unique challenge.
		/// </summary>
		/// <returns></returns>
		public static LatencyRequest Now()
			=> new() {
				InitialTime = DateTime.UtcNow,
				Challenge   = DateTime.UtcNow.Ticks
			};
	}
}