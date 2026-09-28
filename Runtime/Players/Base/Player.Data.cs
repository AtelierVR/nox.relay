using Nox.Entities;

namespace Nox.Relay.Runtime.Players {
	/// <summary>
	/// Persisted data of a player (volume, mute, ...), shared by every partial of <see cref="Player"/>
	/// and across sessions by <c>Nox.Players.Runtime.Main.Get</c>.
	/// </summary>
	public abstract partial class Player {
		/// <summary>Per-player persisted data (volume, mute, ...).</summary>
		public IDataContainer Persistent;

		/// <summary>Dispatches the persisted data changes to the matching partial (voice, ...).</summary>
		private void OnDataChanged(string[] key, object @new, object @old) {
			if (key.Length == 1 && key[0] == "volume")
				OnVolume.Invoke(Volume, EffectiveVolume);
			else if (key.Length == 1 && key[0] == "mute")
				OnMute.Invoke(IsMuted, IsEffectivelyMuted);
		}
	}
}
