using System;
using System.Collections.Generic;
using Nox.Avatars;
using Nox.Avatars.Controllers;
using Nox.CCK.Mods.Cores;
using Nox.CCK.Mods.Events;
using Nox.Controllers;
using Nox.Audio;
using Nox.CCK.Audio;
using Nox.Sessions;
using Nox.Users;
using Nox.Worlds;
using StirlingLabs.MsQuic.Bindings;
using Nox.Players;
using Nox.CCK.Language;

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

		static internal IMainModCoreAPI CoreAPI;
		static internal ChannelRegister VoiceRegister;
		private EventSubscription[] _events = Array.Empty<EventSubscription>();
		private LanguagePack _lang;

		public void OnInitializeMain(IMainModCoreAPI api) {
			CoreAPI = api;

			_lang = api.AssetAPI.GetAsset<LanguagePack>("lang.asset");
			LanguageManager.AddPack(_lang);

			// Preload MsQuic native libraries using LibAPI (mod-aware plugin folders, platform detection, ref-counted loading)
			MsQuic.Init(api.LibAPI.Load);

			// Register voice volume channel (protected from removal until dispose)
			VoiceRegister = new ChannelRegister("voice", new[] { "general" }, api);

			_events = new[] {
				api.EventAPI.Subscribe("controller_changed", OnControllerChanged),
				api.EventAPI.Subscribe("controller_avatar_changed", OnAvatarOfControllerChanged)
			};
			SessionAPI.Register(this);
		}

		private static void OnControllerChanged(EventData context) {
			if (!context.TryGet(0, out IController controller))
				return;
			if (!SessionAPI.TryGet(SessionAPI.Current, out var s))
				return;
			if (s is not Session session)
				return;
			session.OnControllerChanged(controller);
		}

		private static void OnAvatarOfControllerChanged(EventData context) {
			if (!context.TryGet(0, out IControllerAvatar controller))
				return;
			if (!SessionAPI.TryGet(SessionAPI.Current, out var s))
				return;
			if (s is not Session session)
				return;
			session.OnAvatarOfControllerChanged(controller);
		}

		public void OnDisposeMain() {
			VoiceRegister?.Dispose();
			VoiceRegister = null;
			SessionAPI.Unregister(this);
			foreach (var ev in _events)
				CoreAPI.EventAPI.Unsubscribe(ev);
			_events = Array.Empty<EventSubscription>();
			LanguageManager.RemovePack(_lang);
			_lang = null;
			CoreAPI = null;
		}

		internal static IWorldAPI WorldAPI
			=> CoreAPI.ModAPI
				.GetMod("worlds")
				.GetInstance<IWorldAPI>();

		internal static ISessionAPI SessionAPI
			=> CoreAPI.ModAPI
				.GetMod("session")
				.GetInstance<ISessionAPI>();

		internal static IControllerAPI ControllerAPI
			=> CoreAPI.ModAPI
				.GetMod("controllers")
				.GetInstance<IControllerAPI>();

		internal static IUserAPI UserAPI
			=> CoreAPI.ModAPI
				.GetMod("users")
				.GetInstance<IUserAPI>();

		internal static IAvatarAPI AvatarAPI
			=> CoreAPI.ModAPI
				.GetMod("avatar")
				.GetInstance<IAvatarAPI>();

		internal static IMicrophoneAPI MicrophoneAPI
			=> CoreAPI.ModAPI
				.GetMod("microphone")
				.GetInstance<IMicrophoneAPI>();

		internal static IPlayerAPI PlayerAPI
			=> CoreAPI.ModAPI
				.GetMod("players")
				.GetInstance<IPlayerAPI>();
	}
}