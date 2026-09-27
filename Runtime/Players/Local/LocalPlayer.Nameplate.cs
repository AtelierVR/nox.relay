using Nox.CCK.Nameplate;
using Keys = Nox.CCK.Nameplate.Constants;

namespace Nox.Relay.Runtime.Players {
	/// <summary>
	/// Nameplate binding of the relay local player.
	/// <para>
	/// The plate belongs to the active controller (<see cref="INameplateHolder"/>, the client's
	/// handle on the nameplate system): the local player has no physical of its own, so it feeds the
	/// plate's voice image itself, like the offline player does.
	/// </para>
	/// </summary>
	public partial class LocalPlayer {
		/// <summary>
		/// Pushes the level of the captured audio (<see cref="Audio"/>, the microphone bound by the
		/// controller's connector) onto the controller's plate (<c>Keys.VOICE</c>), driving the alpha
		/// of the plate's voice image. Called every frame by <see cref="Update"/>: the plate ignores
		/// an unchanged value.
		/// </summary>
		internal void UpdateNameplate() {
			if (Main.ControllerAPI?.Current is not INameplateHolder holder)
				return;

			var plate = holder.Nameplate;
			if (!plate.IsAlive())
				return;

			plate.Set(Keys.VOICE, Audio?.Level ?? 0f);
		}
	}
}
