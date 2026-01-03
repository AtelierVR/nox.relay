using System;
using System.Net;
using Cysharp.Threading.Tasks;

namespace Nox.Relay.Core.Connectors {
	/// <summary>
	/// Helper methods for connectors.
	/// </summary>
	public static class ConnectorHelper {
		/// <summary>
		/// Try to parse an IPEndPoint from a string in the format "ip:port".
		/// </summary>
		/// <param name="input"></param>
		/// <param name="endPoint"></param>
		/// <returns></returns>
		public static bool TryParseIPEndPoint(string input, out IPEndPoint endPoint) {
			endPoint = null;
			if (string.IsNullOrWhiteSpace(input))
				return false;

			var parts = input.Split(':');
			if (parts.Length != 2)
				return false;

			if (!IPAddress.TryParse(parts[0], out var ip))
				return false;

			if (!ushort.TryParse(parts[1], out var port))
				return false;

			endPoint = new IPEndPoint(ip, port);
			return true;
		}

		public static async UniTask<(string, IPEndPoint)> ParseIPEndPoint(string address) {
			var uri     = new Uri(address);
			var uriType = Uri.CheckHostName(uri.Host);

			switch (uriType) {
				case UriHostNameType.IPv4 or UriHostNameType.IPv6:
					return (uri.Scheme, new IPEndPoint(IPAddress.Parse(uri.Host), uri.Port));
				case UriHostNameType.Dns: {
					var ip = await Dns.GetHostAddressesAsync(uri.Host);
					if (ip.Length > 0)
						return (uri.Scheme, new IPEndPoint(ip[0], uri.Port));
					break;
				}
			}

			return (null, null);
		}


		public static IConnector From(string protocol)
			=> protocol.ToLower() switch {
				TcpConnector.PROTOCOL_NAME => new TcpConnector(),
				UdpConnector.ProtocolName => new UdpConnector(),
				_                         => null
			};
	}
}