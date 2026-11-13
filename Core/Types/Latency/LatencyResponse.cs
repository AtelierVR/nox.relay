using System;
using Nox.CCK.Utils;
using Buffer = Nox.CCK.Utils.Buffer;
using Nox.Relay.Core.Types.Contents;

namespace Nox.Relay.Core.Types.Latency {
	/// <summary>
	/// A response to a latency test request.
	/// </summary>
	public class LatencyResponse : ContentResponse {
		/// <summary>
		/// The time when the latency test was initiated by the client.
		/// </summary>
		public DateTime InitialTime;

		/// <summary>
		/// The time when the latency test reached the server.
		/// </summary>
		public DateTime IntermediateTime;

		/// <summary>
		/// A challenge number used to check if the response corresponds to the request.
		/// see <see cref="LatencyRequest.Challenge"/>.
		/// </summary>
		public long Challenge;

		/// <summary>
		/// Gets the upload latency (time taken to reach the server).
		/// </summary>
		/// <returns></returns>
		public TimeSpan GetUpLatency()
			=> IntermediateTime - InitialTime;

		/// <summary>
		/// Gets the download latency (time taken to return to the client).
		/// </summary>
		/// <returns></returns>
		public TimeSpan GetDownLatency()
			=> ReceivedAt - IntermediateTime;

		/// <summary>
		/// Gets the total latency (round-trip time).
		/// Both ends are precise: <see cref="InitialTime"/> comes back from the relay in Unix
		/// microseconds and <see cref="ContentResponse.ReceivedAt"/> is stamped by the transport
		/// when it read the packet.
		/// <para>
		/// A negative value means the response carries no usable round trip (failed probe, or an
		/// arrival stamp the transport did not set): it must not be read as a 0 ms latency.
		/// </para>
		/// </summary>
		/// <returns></returns>
		public TimeSpan GetLatency()
			=> InitialTime == default || ReceivedAt == default
				? TimeSpan.FromMilliseconds(-1)
				: ReceivedAt - InitialTime;

		public override bool FromBuffer(Buffer buffer) {
			buffer.Start();


			if (buffer.Remaining != 24) {
				Logger.LogWarning($"LatencyResponse buffer size mismatch: expected 24, got {buffer.Remaining} {buffer}");
				return false;
			}

			InitialTime = buffer.ReadDateTime();
			IntermediateTime = buffer.ReadDateTime();
			Challenge = buffer.ReadLong();
			return true;
		}

		public override string ToString()
			=> $"{GetType().Name}[ping={GetLatency().TotalMilliseconds:F3}ms, up={GetUpLatency().TotalMilliseconds:F3}ms, down={GetDownLatency().TotalMilliseconds:F3}ms]";

		/// <summary>
		/// Creates a failed LatencyResponse.
		/// Created when the latency test could not be completed.
		/// </summary>
		/// <returns></returns>
		public static LatencyResponse Failed()
			=> new LatencyResponse {
				InitialTime = DateTime.MinValue,
				IntermediateTime = DateTime.MinValue,
				ReceivedAt = DateTime.MinValue,
				Challenge = -1
			};
	}
}
