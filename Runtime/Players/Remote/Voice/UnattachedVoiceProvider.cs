using Nox.CCK.Utils;
using UnityEngine;

namespace Nox.Relay.Runtime.Players {
	/// <summary>
	/// Fallback output used when nothing can place the speaker: no physical, or one being destroyed by
	/// render culling. The output goes non-spatial, so only a speaker announced as broadcasting is
	/// still heard — a whisper or a normal voice cannot be placed, and playing it from everywhere would
	/// be wrong.
	/// </summary>
	internal sealed class UnattachedVoiceProvider : VoiceProvider {
		private readonly RemotePlayer _player;
		private readonly Transform _holder;
		private readonly AudioSource _source;

		public UnattachedVoiceProvider(RemotePlayer player) {
			_player              = player;
			_holder              = new GameObject($"Voice_{player.Id}").transform;
			_source              = _holder.gameObject.AddComponent<AudioSource>();
			_source.loop         = true;
			_source.playOnAwake  = false;
			_source.spatialBlend = 0f;

			_holder.gameObject.DontDestroyOnLoad();
		}

		public override AudioSource Source
			=> _source;

		public override bool Gated
			=> !_player.VoiceBroadcast;

		/// <summary>Valid as long as the speaker still cannot be placed.</summary>
		public override bool Valid
			=> !_player.VoiceAnchored;

		/// <summary>Nothing to place it on: the output is already non-spatial.</summary>
		public override void Place() { }

		public override void Dispose()
			=> _holder.gameObject.Destroy();
	}
}
