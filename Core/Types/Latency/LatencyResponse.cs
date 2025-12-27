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
		/// The time when the latency test response was sent back to the client.
		/// </summary>
		public DateTime FinalTime;

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
			=> FinalTime - IntermediateTime;

		/// <summary>
		/// Gets the total latency (round-trip time).
		/// </summary>
		/// <returns></returns>
		public TimeSpan GetLatency()
			=> FinalTime - InitialTime;

		public override bool FromBuffer(Buffer buffer) {
			buffer.Start();
			
			if (buffer.Remaining != 24) {
				Logger.LogWarning($"LatencyResponse buffer size mismatch: expected 24, got {buffer.Remaining}");
				return false;
			}

			InitialTime      = buffer.ReadDateTime();
			IntermediateTime = buffer.ReadDateTime();
			FinalTime        = DateTime.UtcNow;
			Challenge        = buffer.ReadLong();
			return true;
		}

		public override string ToString()
			=> $"{GetType().Name}[ping={GetLatency().TotalMilliseconds}ms, up={GetUpLatency().TotalMilliseconds}ms, down={GetDownLatency().TotalMilliseconds}ms]";
	}
}