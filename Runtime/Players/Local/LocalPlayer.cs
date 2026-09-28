using Nox.CCK.Avatars.Voice;
using Nox.CCK.Utils;
using Nox.Audio.Players;
using CorePlayer = Nox.Relay.Core.Players.Player;
using Nox.CCK.Players;
using Nox.Entities;

namespace Nox.Relay.Runtime.Players {
	public partial class LocalPlayer : Player, ILocalPlayerVoice {

		public LocalPlayer(Entities context, CorePlayer player) : base(context, player) {
			GetOrCreatePart(PlayerRig.Base.ToIndex());

			// We are the client: our own platform/engine are the announced ones.
			Platform = PlatformExtensions.CurrentPlatform;
			Engine   = EngineExtensions.CurrentEngine;
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
			UpdateNameplate();
			TickVoice();
		}

		// ── Voice (local) ──

		public override void OnEntered() {
			base.OnEntered();
			StartVoice();

			Data.OnChanged.AddListener(OnEntityDataChanged);
			ApplyToController(Main.ControllerAPI?.Current);
		}

		public override void Dispose() {
			Data.OnChanged.RemoveListener(OnEntityDataChanged);
			base.Dispose();
		}

		public override void OnQuit() {
			StopVoice();
			base.OnQuit();
		}

		public override void OnLeft() {
			StopVoice();
			base.OnLeft();
		}
	}
}