using System.Collections.Generic;
using Nox.Microphone.Players;
using UnityEngine;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>
	/// Per-player hearing/speaking permission sets and distance-based filtering logic.
	/// Managed by <see cref="VoiceManager"/>.
	/// </summary>
	internal class VoicePermissions {
		/// <summary>
		/// Player IDs this player explicitly CAN hear.
		/// When empty (default), all players are hearable (subject to distance/range).
		/// When non-empty, only players in this set are hearable.
		/// </summary>
		internal readonly HashSet<int> HearingAllowList = new();

		/// <summary>
		/// Player IDs who explicitly CAN hear this player.
		/// When empty (default), all players can hear (subject to distance/range).
		/// When non-empty, only players in this set can hear this player.
		/// </summary>
		internal readonly HashSet<int> SpeakingAllowList = new();

		/// <summary>Speaking / emission mode for this player.</summary>
		internal SpeakMode Speak = SpeakMode.Normal;

		/// <summary>Listening / reception mode for this player.</summary>
		internal ListenMode Listen = ListenMode.Normal;

		/// <summary>
		/// Whether this player is currently sending voice.
		/// </summary>
		internal bool Speaking;

		/// <summary>
		/// IDs of players from whom this player is currently receiving voice.
		/// </summary>
		internal readonly HashSet<int> ReceivingFrom = new();

		/// <summary>
		/// Reference to the shared voice config (set by VoiceManager).
		/// </summary>
		internal VoiceConfig Config;

		/// <summary>
		/// Check whether <paramref name="listenerId"/> can hear <paramref name="speakerId"/>.
		/// </summary>
		internal static bool CanHear(
			int listenerId,
			Vector3 listenerPos,
			int speakerId,
			Vector3 speakerPos,
			SpeakMode speakerSpeak,
			VoicePermissions listenerPerms
		) {
			if (listenerId == speakerId)
				return false;

			if (listenerPerms.Listen == ListenMode.Deafen)
				return false;

			if (speakerSpeak == SpeakMode.Muted)
				return false;

			// Explicit hearing allow-list
			if (listenerPerms.HearingAllowList.Count > 0
				&& !listenerPerms.HearingAllowList.Contains(speakerId))
				return false;

			// Distance check (Broadcast = infinite range)
			if (speakerSpeak != SpeakMode.Broadcast && listenerPerms.Config != null) {
				float range = listenerPerms.Config.GetRange(speakerSpeak);
				float dist  = Vector3.Distance(listenerPos, speakerPos);
				if (dist > range)
					return false;
			}

			return true;
		}

		/// <summary>
		/// Check whether <paramref name="speakerId"/> can be heard by <paramref name="listenerId"/>.
		/// Reverse perspective of <see cref="CanHear"/>. Used to compute ListeningPlayers.
		/// </summary>
		internal static bool CanBeHeardBy(
			int speakerId,
			Vector3 speakerPos,
			SpeakMode speakerSpeak,
			int listenerId,
			Vector3 listenerPos,
			VoicePermissions speakerPerms
		) {
			if (speakerId == listenerId)
				return false;

			if (speakerSpeak == SpeakMode.Muted)
				return false;

			// Explicit speaking allow-list
			if (speakerPerms.SpeakingAllowList.Count > 0
				&& !speakerPerms.SpeakingAllowList.Contains(listenerId))
				return false;

			// Distance check
			if (speakerSpeak != SpeakMode.Broadcast && speakerPerms.Config != null) {
				float range = speakerPerms.Config.GetRange(speakerSpeak);
				float dist  = Vector3.Distance(speakerPos, listenerPos);
				if (dist > range)
					return false;
			}

			return true;
		}
	}
}
