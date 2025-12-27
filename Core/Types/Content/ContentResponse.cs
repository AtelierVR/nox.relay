using System;
using Buffer = Nox.CCK.Utils.Buffer;

namespace Nox.Relay.Core.Types.Contents {
	/// <summary>
	/// Base class for all content-related responses in the relay system.
	/// </summary>
	public abstract class ContentResponse {
		/// <summary>
		/// Relay where the packet is received.
		/// </summary>
		public Relay Connection;
		
		/// <summary>
		/// State of the content response.
		/// Is used to indicate the callback state of the initial request.
		/// If is <see cref="Nox.Relay.Networks.Relay.Broadcast">Broadcast</see>, the response is a broadcast message.
		/// </summary>
		public ushort State;

		/// <summary>
		/// Timestamps marking when the request was received and when the response was received.
		/// </summary>
		public (DateTime, DateTime) Time;

		/// <summary>
		/// Populates the current ContentResponse instance from a <see cref="byte[]"/> buffer.
		/// Is recommended to return false if the buffer is invalid or corrupted.
		/// And override should always call <see cref="Buffer.Start"/> before reading from the buffer.
		/// </summary>
		/// <param name="buffer"></param>
		/// <returns></returns>
		public abstract bool FromBuffer(Buffer buffer);
	}
}