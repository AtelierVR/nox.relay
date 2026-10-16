using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Nox.CCK.Utils;
using Nox.Relay.Core.Connectors;
using Nox.Terminal;

namespace Nox.Relay.Runtime.Commands {
	/// <summary>
	/// Terminal command <c>relay</c> — connect to a relay server from the console (including a
	/// standalone relay running without a master node; the client adapts to the server handshake).
	/// <code>
	/// relay connect &lt;host:port&gt; [-w|--world &lt;world&gt;] [-p|--password &lt;password&gt;] [-d|--display &lt;pseudo&gt;]
	/// </code>
	/// </summary>
	public class RelayCommand : ICommand, IHelper {
		public string GetName()
			=> "relay";

		public string GetDescription()
			=> "Connect to a relay server.";

		public string GetShort()
			=> "connect to a relay";

		public string GetUsage()
			=> "relay connect <host:port> [-w|--world <world>] [-p|--password <password>] [-d|--display <pseudo>]";

		public string[] AutoComplete(string input, IContext context = null) {
			var value = input?.ToLowerInvariant() ?? string.Empty;
			if (GetName().StartsWith(value))
				return new[] { GetName() };
			return Array.Empty<string>();
		}

		public UniTask<bool> Execute(string input, IContext context = null)
			=> UniTask.FromResult(ExecuteInternal(input, context));

		private bool ExecuteInternal(string input, IContext context) {
			if (string.IsNullOrWhiteSpace(input) || context == null)
				return false;

			var args = ArgsParser.Parse(input);
			var positional = args.Positional;

			if (positional.Length == 0 || !positional[0].Equals(GetName(), StringComparison.OrdinalIgnoreCase))
				return false;

			if (positional.Length < 3 || !positional[1].Equals("connect", StringComparison.OrdinalIgnoreCase)) {
				context.PrintLn(GetUsage());
				return true;
			}

			var address  = positional[2];
			var world    = args.Get("world") ?? args.Get("w");
			var password = args.Get("password") ?? args.Get("p");
			var display  = args.Get("display") ?? args.Get("d");

			var endpoint = !address.Contains("://", StringComparison.Ordinal)
				? $"{QuicConnector.PROTOCOL_NAME}://{address}"
                : address;

			var options = new Dictionary<string, object> {
				["set_current"] = true,
				["title"]       = $"Relay {address}",
				["short_name"]  = address,
				["data"]        = new JObject { ["a"] = new JArray(endpoint) },
			};
			if (!string.IsNullOrEmpty(display))
				options["display"] = display; // in-room pseudonym (EnterRequest.Display)
			if (!string.IsNullOrEmpty(world))
				try {
					options["world"] = Identifier.Parse(world);
				} catch (Exception e) {
					context.PrintLn($"Invalid world identifier '{world}': {e.Message}");
					return true;
				}
			if (!string.IsNullOrEmpty(password))
				options["password"] = password;

			var api = Main.SessionAPI;
			if (api == null || !api.TryMake("relay", options, out var session) || session == null) {
				context.PrintLn($"Failed to create a relay session for '{address}'.");
				return true;
			}

			context.PrintLn($"Connecting to relay '{address}'...");
			return true;
		}
	}
}
