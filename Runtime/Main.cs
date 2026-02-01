using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Nox.Avatars;
using Nox.CCK.Mods.Cores;
using Nox.CCK.Mods.Events;
using Nox.Controllers;
using Nox.Relay.Core.Connectors;
using Nox.Relay.Core.Types.Latency;
using Nox.Sessions;
using Nox.Users;
using Nox.Worlds;

namespace Nox.Relay.Runtime {
	public class Main : ISessionRegister {
		public bool TryMakeSession(string name, Dictionary<string, object> options, out ISession session) {
			if (name != "relay" && name != "external:relay") {
				session = null;
				return false;
			}

			session = Helper.Create(Options.From(options));
			return true;
		}

		internal static IMainModCoreAPI CoreAPI;
		private EventSubscription[] _events = Array.Empty<EventSubscription>();

		public void OnInitializeMain(IMainModCoreAPI api) {
			CoreAPI = api;
			_events = new[] {
				api.EventAPI.Subscribe("controller_changed", OnControllerChanged)
			};
			SessionAPI.Register(this);

			#if UNITY_EDITOR
			CoreAPI.LoggerAPI.Log("Starting relay tests...");
			TestRelay().Forget();
			#endif
		}

		private static async UniTask TestRelay() {
			var relay = Core.Relay.New<TcpConnector>();

			if (!await relay.Connect("hactazia.fr", 30000)) {
				CoreAPI.LoggerAPI.LogError("Failed to connect to relay server");
				return;
			}

			CoreAPI.LoggerAPI.Log("Connected to relay server");

			var handshake = await relay.Handshake();
			if (handshake == null) {
				CoreAPI.LoggerAPI.LogError("Relay handshake failed");
				await relay.Disconnect("Handshake failed");
				return;
			}

			CoreAPI.LoggerAPI.Log($"{handshake}");

			var ls = new List<LatencyResponse>();
			for (var i = 0; i < 100; i++) {
				var latency = await relay.Latency();
				if (latency == null) {
					CoreAPI.LoggerAPI.LogError("Failed to measure relay latency");
					await relay.Disconnect("Latency test failed");
					return;
				}

				ls.Add(latency);
				await UniTask.Delay(100);
			}

			var avgLatency = 0d;
			var maxLatency = double.MinValue;
			var minLatency = double.MaxValue;

			foreach (var l in ls) {
				var v = l.GetLatency().TotalMilliseconds;
				avgLatency += v;
				if (v > maxLatency)
					maxLatency = v;
				if (v < minLatency)
					minLatency = v;
			}

			avgLatency /= ls.Count;

			CoreAPI.LoggerAPI.Log(
				$"Relay latency over {ls.Count} tests: "
				+ $"avg={avgLatency:F2}ms, "
				+ $"min={minLatency:F2}ms, "
				+ $"max={maxLatency:F2}ms"
			);

			var rooms = await relay.List();
			if (rooms == null) {
				CoreAPI.LoggerAPI.LogError("Failed to list relay rooms");
				await relay.Disconnect("List rooms failed");
				return;
			}

			foreach (var room in rooms.Rooms)
				CoreAPI.LoggerAPI.Log($"Found room: {room}");

			// var tasks = new List<UniTask<LatencyResponse>>();
			// for (var i = 0; i < 1000; i++)
			// 	tasks.Add(relay.Latency());
			// var results = await UniTask.WhenAll(tasks);
			// foreach (var result in results)
			// 	if (result == null) {
			// 		CoreAPI.LoggerAPI.LogError("Failed to measure relay latency in batch");
			// 		await relay.Disconnect("Latency batch test failed");
			// 		return;
			// 	}

			await relay.Disconnect("Done testing");
			CoreAPI.LoggerAPI.Log("Disconnected from relay server");
		}

		private static void OnControllerChanged(EventData context) {
			if (!context.TryGet(0, out IController controller)) return;
			if (!SessionAPI.TryGet(SessionAPI.Current, out var s)) return;
			if (s is not Session session) return;
			session.OnControllerChanged(controller);
		}

		public void OnDisposeMain() {
			SessionAPI.Unregister(this);
			foreach (var ev in _events)
				CoreAPI.EventAPI.Unsubscribe(ev);
			_events = Array.Empty<EventSubscription>();
			CoreAPI = null;
		}

		internal static IWorldAPI WorldAPI
			=> CoreAPI.ModAPI
				.GetMod("world")
				.GetInstance<IWorldAPI>();

		internal static ISessionAPI SessionAPI
			=> CoreAPI.ModAPI
				.GetMod("session")
				.GetInstance<ISessionAPI>();

		internal static IControllerAPI ControllerAPI
			=> CoreAPI.ModAPI
				.GetMod("controller")
				.GetInstance<IControllerAPI>();

		internal static IUserAPI UserAPI
			=> CoreAPI.ModAPI
				.GetMod("user")
				.GetInstance<IUserAPI>();

		internal static IAvatarAPI AvatarAPI
			=> CoreAPI.ModAPI
				.GetMod("avatar")
				.GetInstance<IAvatarAPI>();
	}
}