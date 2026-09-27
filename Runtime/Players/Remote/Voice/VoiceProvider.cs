using UnityEngine;

namespace Nox.Relay.Runtime.Players {
	/// <summary>
	/// Where a remote player's voice clip is played, and how it is gated. A provider owns everything
	/// its output needs — the object, the <see cref="AudioSource"/> and the anchor it follows — and
	/// builds it in its constructor, so the player keeps no output state of its own.
	/// </summary>
	/// <remarks>
	/// The session-side pipeline (jitter buffer, Opus decoding, read-head pacing) is provider-agnostic:
	/// a provider only decides which <see cref="AudioSource"/> receives the clip, how that output is
	/// placed each frame, and whether it must stay silent.
	/// </remarks>
	internal abstract class VoiceProvider {
		/// <summary>
		/// Output the clip plays on, or <c>null</c> when this provider has nothing to play on right
		/// now — an avatar whose source is gone, for instance.
		/// </summary>
		public abstract AudioSource Source { get; }

		/// <summary>True while this provider must silence its output on top of the player's own mute.</summary>
		public abstract bool Gated { get; }

		/// <summary>False once the provider no longer matches its player, and must be replaced.</summary>
		public abstract bool Valid { get; }

		/// <summary>Places the output for this frame.</summary>
		public abstract void Place();

		/// <summary>Releases what the provider owns; called when it is replaced or the voice stops.</summary>
		public virtual void Dispose() { }

		/// <summary>Provider for the player's current state, owning its own output.</summary>
		public static VoiceProvider Create(RemotePlayer player) {
			if (!player.VoiceAnchored)
				return new UnattachedVoiceProvider(player);

			return AvatarVoiceProvider.TryCreate(player, out var provider)
				? provider
				: new PhysicalVoiceProvider(player);
		}
	}
}
