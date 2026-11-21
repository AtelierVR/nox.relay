using System;
using System.Net;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Nox.Relay.Core.Connectors {
	/// <summary>
	/// Helper methods for connectors.
	/// </summary>
	public static class ConnectorHelper {
		/// <summary>
		/// Port used when an address does not name one: <see cref="Uri.Port"/> is <c>-1</c> for a scheme
		/// that declares no default port (and <see cref="IPEndPoint"/> rejects it), so the relay default is
		/// used instead of failing the connection.
		/// </summary>
		public const int DEFAULT_PORT = 23032;

		/// <summary>Port named by a URI, or <see cref="DEFAULT_PORT"/> when it declares none.</summary>
		private static int PortOf(Uri uri)
			=> uri.Port > 0 ? uri.Port : DEFAULT_PORT;

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

		public static async UniTask<(string, string, IPEndPoint)> ParseIPEndPoint(string address, CancellationToken token = default) {
			token.ThrowIfCancellationRequested();

			if (string.IsNullOrWhiteSpace(address))
				return (null, null, null);

			// A bare "host[:port]" is accepted: the scheme is only used to pick the connector.
			if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
				return (null, null, null);

			var uriType = Uri.CheckHostName(uri.Host);

			switch (uriType) {
				case UriHostNameType.IPv4 or UriHostNameType.IPv6:
					return (uri.Scheme, uri.Host, new IPEndPoint(IPAddress.Parse(uri.Host), PortOf(uri)));
				case UriHostNameType.Dns: {
					var ip = await Dns.GetHostAddressesAsync(uri.Host);
					token.ThrowIfCancellationRequested();
					if (ip.Length > 0)
						return (uri.Scheme, uri.Host, new IPEndPoint(ip[0], PortOf(uri)));
					break;
				}
			}

			return (null, null, null);
		}


		public static IConnector From(string protocol)
			=> protocol.ToLower() switch {
				QuicConnector.PROTOCOL_NAME => new QuicConnector(),
				#pragma warning disable CS0618 // legacy connectors kept for compile compatibility
				TcpConnector.PROTOCOL_NAME => new TcpConnector(),
				UdpConnector.PROTOCOL_NAME  => new UdpConnector(),
				#pragma warning restore CS0618
				_ => null
			};
	}
}
