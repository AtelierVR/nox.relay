using System.Collections.Generic;
using Nox.Audio.Players;
using Nox.CCK.Nameplate;
using Nox.CCK.Utils;
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
		/// Pushes the level of the captured audio (<see cref="Audio"/>, the microphone bound by
		/// <see cref="StartVoice"/>) onto the controller's plate (<c>Keys.VOICE</c>), driving the alpha
		/// of the plate's voice image. Called every frame by <see cref="Update"/>: the plate ignores an
		/// unchanged value.
		/// <para>
		/// The voice status (<c>Keys.STATUS</c>) is pushed by <see cref="PushNameplateStatus"/> when the
		/// state actually changes, never per frame.
		/// </para>
		/// </summary>
		internal void UpdateNameplate() {
			if (Main.ControllerAPI?.Current is not INameplateHolder holder)
				return;

			var plate = holder.Nameplate;
			if (!plate.IsAlive())
				return;

			plate.Set(Keys.VOICE, Audio?.Level ?? 0f);
		}

		/// <summary>
		/// Pushes the voice status of the local player onto the controller's plate (<c>Keys.STATUS</c>):
		/// muted while self-muted, deafened while the voice channel is muted, plus the special icon of
		/// a non-normal speak mode. Called when the state changes (self-mute, deafen, speak mode) and
		/// when the plate is taken (see <see cref="ApplyToController"/>).
		/// </summary>
		internal void PushNameplateStatus() {
			if (Main.ControllerAPI?.Current is not INameplateHolder holder)
				return;

			var plate = holder.Nameplate;
			if (!plate.IsAlive())
				return;

			var muted    = IsSelfMuted;
			var deafened = IsDeafened;
			var mode     = DesiredSpeak;

			var status = new Dictionary<string, string>(3);
			if (muted)
				status[NameplateExtensions.VoiceIcon("mute")] = "mute";
			if (deafened)
				status[NameplateExtensions.StatusIcon("deaf")] = "deaf";

			if (mode is >= SpeakMode.Whisper and <= SpeakMode.Broadcast && mode != SpeakMode.Normal) {
				var key = mode.ToString().ToSnakeCase();
				status[NameplateExtensions.VoiceIcon(key)] = key;
			}

			plate.Set(Keys.STATUS, status);
		}

		private void OnNameplateVoiceStatusChanged(bool _)
			=> PushNameplateStatus();
	}
}
