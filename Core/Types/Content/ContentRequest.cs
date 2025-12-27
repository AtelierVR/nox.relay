using Nox.CCK.Utils;

namespace Nox.Relay.Core.Types.Contents {
	/// <summary>
	/// Base class for all content-related requests in the relay system.
	/// </summary>
	public abstract class ContentRequest {
		/// <summary>
		/// Converts the current ContentRequest instance into a <see cref="byte[]"/> buffer.
		/// </summary>
		/// <returns></returns>
		public abstract Buffer ToBuffer();
	}
}