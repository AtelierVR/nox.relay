using System.Net;

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
	}
}