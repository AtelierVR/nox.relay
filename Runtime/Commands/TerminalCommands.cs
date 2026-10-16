using System;
using Nox.CCK.Mods.Cores;
using Nox.Terminal;

namespace Nox.Relay.Runtime.Commands {
	/// <summary>
	/// Registers the relay terminal commands (and unregisters them on dispose). Mirrors the pattern
	/// used by the other mods (see nox.sessions).
	/// </summary>
	public class TerminalCommands : IDisposable {
		private readonly IMainModCoreAPI _api;

		private readonly (uint, ICommand)[] _list = { 
            (0, new RelayCommand())
        };

		public TerminalCommands(IMainModCoreAPI coreAPI) {
			_api = coreAPI;

			var api = TerminalAPI;
			if (api == null)
				return;

			for (var i = 0; i < _list.Length; i++)
				_list[i] = (api.Register(_list[i].Item2), _list[i].Item2);
		}

		internal ITerminalAPI TerminalAPI
			=> _api?.ModAPI
                ?.GetMod("terminal")
                ?.GetInstance<ITerminalAPI>();

		public void Dispose() {
			var api = TerminalAPI;
			if (api == null)
				return;

			for (var i = 0; i < _list.Length; i++)
				api.Unregister(_list[i].Item1);
		}
	}
}
