using Nox.Avatars;
using Nox.Avatars.Parameters;
using Nox.CCK.Avatars.Voice;
using Nox.CCK.Events;
using Nox.Audio.Players;
using Nox.Relay.Runtime.Voice;
using CorePlayer = Nox.Relay.Core.Players.Player;
using Nox.CCK.Players;
using Nox.Entities;

namespace Nox.Relay.Runtime.Players {
	public partial class LocalPlayer : Player, ILocalPlayerVoice {

		public LocalPlayer(Entities context, CorePlayer player) : base(context, player) {
			VoiceProvider = new LocalVoiceProvider(this);
			GetOrCreatePart(PlayerRig.Base.ToIndex());
		}

		protected override IPart CreatePart(ushort index)
			=> new Part(this, index);

		/// <summary>
		/// The live <see cref="ICapturedAudio"/> for this local player.
		/// Setting it automatically routes the clip to the current avatar's <see cref="VoiceAvatarModule"/>
		/// and syncs the AudioSource playback position to the mic write head (zero latency monitor).
		/// </summary>
		public new ICapturedAudio Audio {
			get => _audio;
			set {
				_audio = value;
				// RouteClipToAvatar();
			}
		}

		public override bool IsLocal
			=> true;

		public override void Update() {
			// Disabled for local player - no interpolation needed
		}

		// ── Voice (local) ──

		public override void OnEntered() {
			base.OnEntered();
			VoiceProvider.Initialize();
		}

		public override void OnQuit() {
			VoiceProvider.Dispose();
			base.OnQuit();
		}

		public override void OnLeft() {
			VoiceProvider.Dispose();
			base.OnLeft();
		}
	}
}