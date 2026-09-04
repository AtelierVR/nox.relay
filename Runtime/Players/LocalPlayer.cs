using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using Nox.Avatars;
using Nox.Avatars.Controllers;
using Nox.Avatars.Parameters;
using Nox.CCK.Avatars.Voice;
using Nox.CCK.Events;
using Nox.Controllers;
using Nox.Audio.Players;
using Nox.Relay.Core.Types.Avatars;
using Nox.Relay.Runtime.Voice;
using Logger = Nox.CCK.Utils.Logger;
using CorePlayer = Nox.Relay.Core.Players.Player;
using Nox.CCK.Players;
using Nox.Entities;

namespace Nox.Relay.Runtime.Players {
	public class LocalPlayer : Player, ILocalPlayerVoice {
		/// <summary>Fired when the local player's avatar is loaded or changed via controller.</summary>
		public readonly NoxEvent<IRuntimeAvatar> OnAvatarLoaded = new();

		public LocalPlayer(Entities context, CorePlayer player) : base(context, player) {
			VoiceProvider = new LocalVoiceProvider(this);
			GetOrCreatePart(PlayerRig.Base.ToIndex());
		}

		protected override IPart CreatePart(ushort index)
			=> new Part(this, index);

		private IRuntimeAvatar _currentAvatar;

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

		// Note: Tick() is inherited from Player.cs and handles SendTransformsIfNeeded()

		internal void UpdateController(IController controller) {
			if (controller == null) {
				RemoveController();
				return;
			}

			var cParts = controller.GetParts();

			// Remove parts that no longer exist in the controller
			var keysToRemove = Parts.Keys.Except(cParts.Select(p => p.Key)).ToList();
			foreach (var key in keysToRemove)
				Parts.Remove(key);

			// Add new parts from the controller (initialize cache)
			foreach (var cPart in cParts) {
				if (Parts.ContainsKey(cPart.Key))
					continue;
				Parts[cPart.Key] = new Part(this, cPart.Key);
			}

			// Initialize each part's cache with current controller values
			foreach (var part in Parts.Values)
				if (part is Part p)
					p.Restore(controller);

			// Synchronize avatar parameters as properties
			if (controller is IControllerAvatar avatarController)
				UpdateAvatarOfController(avatarController);
		}

		internal void RemoveController() {
			foreach (var part in Parts.Values)
				if (part is Part p)
					p.Store();
		}

		/// <summary>
		/// Synchronizes avatar parameters as properties on the entity.
		/// Uses the base class SynchronizeAvatarParameters() method.
		/// </summary>
		internal void UpdateAvatarOfController(IControllerAvatar controller)
			=> UpdateAvatarOfControllerAsync(controller).Forget();

		private async UniTask UpdateAvatarOfControllerAsync(IControllerAvatar controller) {
			var avatar   = controller.GetAvatar();
			var response = await Context.Context.Room.ChangeAvatar(AvatarChangedRequest.Self(avatar.Identifier));
			if (response.IsError) {
				Logger.LogWarning($"Failed to change avatar: {response.Reason}");
				return;
			}

			var descriptor = avatar?.Descriptor;
			var parameterModule = descriptor
				?.GetModules<IParameterModule>()
				.FirstOrDefault();

			var parameters = parameterModule
					?.GetParameters()
				?? Array.Empty<IParameter>();

			SynchronizeAvatarParameters(parameters, isLocal: true);

			if (avatar != null) {
				_currentAvatar = avatar;
				// RouteClipToAvatar();
				OnAvatarLoaded.Invoke(avatar);
			}
		}

		// private void RouteClipToAvatar() {
		// 	var module = _currentAvatar?.Descriptor
		// 		?.GetModules<VoiceAvatarModule>()
		// 		.FirstOrDefault();
		// 	var source = module?.GetSource();
		// 	if (!source) return;

		// 	var clip = _audio?.Clip;
		// 	source.loop = true;

		// 	if (clip == null) {
		// 		source.Stop();
		// 		source.clip = null;
		// 		return;
		// 	}

		// 	// Do not restart an already-playing source with the same clip:
		// 	// restarting causes an audible gap/hole in the monitored audio.
		// 	if (source.clip == clip && source.isPlaying)
		// 		return;

		// 	source.clip = clip;
		// 	// Sync playback head to live mic write position — eliminates monitoring latency.
		// 	source.timeSamples = _audio.GetPosition();
		// 	source.Play();
		// }
	}
}