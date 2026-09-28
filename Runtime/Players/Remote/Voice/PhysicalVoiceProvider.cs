using Nox.CCK.Utils;
using UnityEngine;

namespace Nox.Relay.Runtime.Players {
	/// <summary>
	/// Fallback output placed at the speaker's mouth on the physical, and spatialized. Used while the
	/// avatar publishes no source of its own.
	/// </summary>
	/// <remarks>
	/// The output lives in its own root object rather than under the physical — render culling
	/// destroys the physical, and a child would be deactivated and destroyed with it — so the provider
	/// follows the physical every frame.
	/// </remarks>
	internal sealed class PhysicalVoiceProvider : VoiceProvider {
		/// <summary>Height above the physical origin where the voice source is placed.</summary>
		private const float AnchorHeight = 1.6f;

		private readonly RemotePlayer _player;
		private readonly Transform _holder;
		private readonly AudioSource _source;

		public PhysicalVoiceProvider(RemotePlayer player) {
			_player              = player;
			_holder              = new GameObject($"Voice_{player.Id}").transform;
			_source              = _holder.gameObject.AddComponent<AudioSource>();
			_source.loop         = true;
			_source.playOnAwake  = false;
			_source.spatialBlend = 1f;

			_holder.gameObject.DontDestroyOnLoad();
		}

		public override AudioSource Source
			=> _source;

		public override bool Gated
			=> false;

		/// <summary>A live physical is exactly what this provider needs to stay valid.</summary>
		public override bool Valid
			=> _player.VoiceAnchored;

		public override void Place() {
			var anchor = _player.VoiceAnchor;
			if (anchor == null)
				return;

			_holder.SetPositionAndRotation(
				anchor.TransformPoint(Vector3.up * AnchorHeight),
				anchor.rotation
			);
		}

		public override void Dispose()
			=> _holder.gameObject.Destroy();
	}
}
