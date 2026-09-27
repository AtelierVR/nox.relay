using Nox.Avatars.Voice;
using Nox.Relay.Runtime.Physicals;
using UnityEngine;

namespace Nox.Relay.Runtime.Players {
	/// <summary>
	/// Plays the voice clip on the source the avatar publishes through its <see cref="IVoiceModule"/>.
	/// The module already places that source on the head bone, so the provider only resolves it once
	/// and keeps it spatial — it owns nothing.
	/// </summary>
	internal sealed class AvatarVoiceProvider : VoiceProvider {
		private readonly RemotePlayer _player;
		private readonly AudioSource _source;

		private AvatarVoiceProvider(RemotePlayer player, AudioSource source) {
			_player = player;
			_source = source;
		}

		public override AudioSource Source
			=> _source;

		public override bool Gated
			=> false;

		/// <summary>Destroying the avatar takes its module — and the source — with it.</summary>
		public override bool Valid
			=> _source != null && _player.VoiceAnchored;

		public override void Place() {
			if (_source != null)
				_source.spatialBlend = 1f;
		}

		/// <summary>Creates the provider when the physical's avatar publishes an output.</summary>
		public static bool TryCreate(RemotePlayer player, out VoiceProvider provider) {
			var module = player.TryGetPhysical<RemotePhysical>(out var physical)
				? physical.GetComponentInChildren<IVoiceModule>(true)
				: null;
			var source = module?.GetSource();

			provider = source != null ? new AvatarVoiceProvider(player, source) : null;
			return provider != null;
		}
	}
}
