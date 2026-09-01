using System.Net;
using Nox.CCK.Utils;
using Nox.Relay.Core.Types.Contents;

namespace Nox.Relay.Core.Types.Handshakes {
	/// <summary>
	/// Relay Request Handshake
	/// Represents a handshake request sent by a client to initiate communication with the relay server.
	/// </summary>
	public class HandshakeRequest : ContentRequest {
		/// <summary>
		/// Protocol Version
		/// The version of the protocol being used by the client.
		/// </summary>
		public ushort ProtocolVersion;

		public EndPoint Endpoint;

		/// <summary>
		/// Engine
		/// The game engine used by the client (e.g., Unity, Unreal).
		/// for more details, see <see cref="Engine"/>.
		/// </summary>
		public Engine Engine;

		/// <summary>
		/// Platform
		/// The platform on which the client is running (e.g., Windows, Mac, Linux).
		/// for more details, see <see cref="Platform"/>.
		/// </summary>
		public Platform Platform;

		public override Buffer ToBuffer() {
			var buffer = new Buffer();
			buffer.Write(ProtocolVersion);
			if (Endpoint is IPEndPoint ip) {
				buffer.Write(ip.Address.ToString());
				buffer.Write((ushort)ip.Port);
			} else if (Endpoint is DnsEndPoint host) {
				buffer.Write(host.Host);
				buffer.Write((ushort)host.Port);
			} else {
				Logger.LogWarning($"Endpoint '{Endpoint}' {Endpoint?.GetType().Name ?? "null"} is not a valid {nameof(EndPoint)} type.");
				buffer.Write(string.Empty);
				buffer.Write(ushort.MinValue);
			}
			buffer.Write(Engine.GetEngineName());
			buffer.Write(Platform.GetPlatformName());
			return buffer;
		}
	}
}