using System;
using Cysharp.Threading.Tasks;
using Nox.Audio;
using Nox.Audio.Players;
using Nox.CCK;
using Nox.CCK.Audio;
using Nox.CCK.Audio.Opus;
using Nox.CCK.Mods.Events;
using Nox.Relay.Core.Types.Stream;
using UnityEngine;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Relay.Runtime.Players {
	/// <summary>
	/// Voice of the local player: owns the microphone capture bound to this player and encodes it with
	/// an <see cref="OpusAudioSender"/>, emitting one datagram per encoded frame to the room (channel
	/// <see cref="ChannelId.Voice"/>).
	/// </summary>
	/// <remarks>
	/// The capture is bound here rather than pushed by a controller connector: the player is the one
	/// that knows when it enters or leaves the room, and it must follow the audio mod's current
	/// microphone (device switch, unplug, mute) for as long as it stays entered.
	/// </remarks>
	public partial class LocalPlayer {
		/// <summary>Key the device is started/stopped with, so it can be shared with other consumers.</summary>
		private const string MicrophoneOwner = "voice";

		/// <summary>Device currently capturing for this player, or null while none is bound.</summary>
		private IMicrophone _microphone;

		/// <summary>Subscriptions to the microphone and speak-mode events.</summary>
		private EventSubscription[] _voiceEvents = Array.Empty<EventSubscription>();

		private VoiceModeState _modeState = new() {
			Desired = SpeakMode.Normal,
			AllowedModes = VoiceModeSettings.ALL_SPEAK_MODES,
		};

		/// <inheritdoc/>
		public SpeakMode DesiredSpeak
			=> _modeState.Desired;

		/// <summary>Set once the "no microphone" warning was emitted, to keep the per-frame retry quiet.</summary>
		private bool _microphoneWarned;

		private VoicePipeline _pipeline;

		/// <summary>The local player is muted when its microphone capture is paused.</summary>
		public override bool IsSelfMuted
			=> Speak == SpeakMode.Muted;

		/// <summary>Deafen is the voice channel being silent (muted, muted by a parent, or at volume 0).</summary>
		public override bool IsDeafened
			=> Main.AudioAPI?.GetChannel("voice")?.IsSilent() ?? false;

		/// <summary>The audio mod's microphone registry, or null while that mod is not loaded.</summary>
		private static IMicrophoneAPI MicrophoneAPI
			=> Main.CoreAPI?.ModAPI
				?.GetMod("audio")
				?.GetInstance<IMicrophoneAPI>();

		/// <summary>
		/// Binds the current microphone and starts the emission pipeline. Idempotent, and safe to call
		/// before a device exists: <see cref="TickVoice"/> retries until one shows up.
		/// </summary>
		internal void StartVoice() {
			ResubscribeVoiceEvents();
			RefreshSessionVoice();

			if (_pipeline.Sender != null)
				return;

			BindMicrophone();

			StartSender();
		}

		/// <summary>Stops the capture and releases the emission pipeline. Safe to call when it never started.</summary>
		internal void StopVoice() {
			_pipeline.Sender?.Dispose();
			_pipeline.Sender = null;

			UnsubscribeVoiceEvents();
			UnbindMicrophone();

			// Nothing is captured anymore: stay silent until a device is bound again.
			Speak = SpeakMode.Muted;
		}

		/// <summary>
		/// Drains the newly captured audio once per frame (called from <see cref="LocalPlayer.Update"/>).
		/// The capture is bound lazily, since the audio mod or a device may be missing when the player
		/// entered the room.
		/// </summary>
		internal void TickVoice() {
			if (_pipeline.Sender == null) {
				StartVoice();
				if (_pipeline.Sender == null)
					return;
			} else {
				// The transport payload moves with the path MTU (QUIC re-negotiates it live): the emitter
				// re-sizes itself in place, and ignores a budget that did not change.
				_pipeline.Sender.MaxPayloadSize = VoicePayloadBudget;
			}

			// Muted: keep the pipeline alive but emit nothing.
			if (Speak == SpeakMode.Muted) {
				_pipeline.Sender.Pause();
				return;
			}

			// Resume() re-arms the read cursor: calling it while already running would reset that
			// cursor every frame, and Tick() would never see a single new sample.
			if (!_pipeline.Sender.IsRunning)
				_pipeline.Sender.Resume();

			_pipeline.Sender.Tick();
		}

		// ── Speak mode ────────────────────────────────────────────────────────

		private void ResubscribeVoiceEvents() {
			UnsubscribeVoiceEvents();

			if (Main.CoreAPI == null)
				return;

			var api = Main.CoreAPI.EventAPI;
			var events = _microphone == null
				? new EventSubscription[1]
				: new EventSubscription[5];

			events[0] = api.Subscribe("voice.speak_mode.next", OnNextSpeakMode);

			if (_microphone != null) {
				events[1] = api.Subscribe("audio.microphone.current_changed", OnMicrophoneCurrentChanged);
				events[2] = api.Subscribe("audio.microphone.mute_changed", OnMicrophoneMuteChanged);
				events[3] = api.Subscribe("audio.microphone.volume_changed", OnMicrophoneVolumeChanged);
				events[4] = api.Subscribe("audio.microphone.removed", OnMicrophoneRemoved);
			}

			_voiceEvents = events;
			Context.Context?.Data?.OnChanged.AddListener(OnSessionDataChanged);
		}

		private void UnsubscribeVoiceEvents() {
			if (_voiceEvents.Length == 0)
				return;

			if (Main.CoreAPI != null)
				foreach (var subscription in _voiceEvents)
					Main.CoreAPI.EventAPI.Unsubscribe(subscription);

			_voiceEvents = Array.Empty<EventSubscription>();
			Context.Context?.Data?.OnChanged.RemoveListener(OnSessionDataChanged);
		}

		/// <summary>
		/// Switches to the next mode of the cycle. A muted device keeps announcing
		/// <see cref="SpeakMode.Muted"/>: only the mode restored on unmute moves.
		/// </summary>
		private void OnNextSpeakMode(EventData context) {
			var next = Next(_modeState.Desired);

			if (!VoiceModeSettings.IsAllowed(next, _modeState.AllowedModes))
				next = SpeakMode.Normal;

			SetDesiredSpeak(next);
		}

		private static SpeakMode Next(SpeakMode mode) {
			if (mode is < SpeakMode.Whisper or > SpeakMode.Broadcast)
				return SpeakMode.Normal;

			return mode == SpeakMode.Broadcast
				? SpeakMode.Whisper
				: (SpeakMode)((int)mode + 1);
		}

		private void OnSessionDataChanged(string[] key, object value, object previous) {
			if (key is not { Length: > 0 })
				return;

			var name = string.Join(".", key);
			if (name != VoiceModeSettings.SPEAK_MODE_KEY
				&& name != VoiceModeSettings.ALLOWED_MODES_KEY
				&& name != VoiceModeSettings.FORCE_MUTE_KEY)
				return;

			RefreshSessionVoice();
		}

		private void RefreshSessionVoice() {
			var data = Context.Context?.Data;

			_modeState.ForceMute = data?.Get(VoiceModeSettings.FORCE_MUTE_KEY, false) ?? false;
			_modeState.AllowedModes = data?.Get(VoiceModeSettings.ALLOWED_MODES_KEY, VoiceModeSettings.ALL_SPEAK_MODES) ?? VoiceModeSettings.ALL_SPEAK_MODES;
			_modeState.Session = data != null && data.Has(VoiceModeSettings.SPEAK_MODE_KEY)
				? VoiceModeSettings.ParseMode(data.Get<object>(VoiceModeSettings.SPEAK_MODE_KEY))
				: null;

			SetDesiredSpeak(VoiceModeSettings.Resolve(_modeState.Session, VoiceModeSettings.ChangeOnJoin, _modeState.AllowedModes));
		}

		private void SetDesiredSpeak(SpeakMode mode) {
			_modeState.Desired = mode;
			ApplySpeakState();
			PushNameplateStatus();

			if (_modeState.Announced == mode)
				return;

			_modeState.Announced = mode;
			Main.CoreAPI?.EventAPI.Emit("voice.speak_mode_changed", this, mode);
		}

		private void ApplySpeakState() {
			var muted = _modeState.ForceMute || _microphone == null || _microphone.IsSilent();
			var wanted = muted
				? SpeakMode.Muted
				: _modeState.Desired;

			if (Speak != wanted)
				Speak = wanted;
		}

		// ── Microphone capture ────────────────────────────────────────────────

		/// <summary>Binds the audio mod's current microphone, falling back to its default one.</summary>
		private void BindMicrophone() {
			var api = MicrophoneAPI;
			var microphone = api?.Current ?? api?.Default;

			if (microphone == null) {
				if (!_microphoneWarned) {
					_microphoneWarned = true;
					Logger.LogWarning("No microphone available: the local player stays silent until one is.");
				}
				return;
			}

			_microphoneWarned = false;
			BindMicrophone(microphone);
		}

		/// <summary>Starts <paramref name="microphone"/> and exposes it as this player's <see cref="Audio"/>.</summary>
		private void BindMicrophone(IMicrophone microphone) {
			if (_microphone == microphone)
				return;

			UnbindMicrophone();

			if (microphone == null) {
				ResubscribeVoiceEvents();
				return;
			}

			_microphone = microphone;

			var clip = microphone.Start(MicrophoneOwner);
			Audio = clip ? new CapturedMicrophone(clip, microphone) : null;

			ResubscribeVoiceEvents();

			// The emitter reads the capture it was built with, and a device switch replaced it: rebuild.
			_pipeline.Sender?.Dispose();
			_pipeline.Sender = null;
			StartSender();

			// The new device may already be muted (or at volume 0): announce its state right away.
			ApplyMicrophoneSilence();
		}

		/// <summary>Stops the bound device, unsubscribes its events and forgets the capture.</summary>
		private void UnbindMicrophone() {
			_microphone?.Stop(MicrophoneOwner);
			_microphone = null;
			Audio = null;
		}

		/// <summary>
		/// Usable payload per datagram reported by the transport, or 0 while it is not known yet (no
		/// handshake, not connected). QUIC keeps it in sync with the path MTU, so it can move live.
		/// </summary>
		private int VoiceMtu
			=> Context.Context.Adapter?.Connector?.Mtu ?? 0;

		/// <summary>
		/// Bytes a single voice datagram may give to the encoded sample: the transport's usable payload
		/// (<see cref="Nox.Relay.Core.Connectors.IConnector.Mtu"/>, which already excludes the headers the
		/// transport adds itself) minus the relay, room and stream framing.
		/// </summary>
		private int VoicePayloadBudget
			=> OpusConfig.ClampPayload(
				VoiceMtu <= 0
					? OpusConfig.MaxPayload
					: VoiceMtu - StreamRequest.FramingOverhead()
			);

		/// <summary>Creates the emitter once a capture is available; no-op otherwise.</summary>
		private void StartSender() {
			if (_pipeline.Sender != null || Audio?.Clip == null)
				return;

			var settings = OpusAudioSender.Settings.FromBudget(VoicePayloadBudget);

			_pipeline.Index = 0;
			_pipeline.StartedAt = Time.time;
			_pipeline.Sender = new OpusAudioSender(Audio, SendVoicePacket, settings);

			Logger.LogDebug(
				$"Voice emitter: {settings.Bitrate / 1000} kbps, {settings.FrameDurationMs} ms frames, "
				+ $"{settings.MaxPayloadSize} B payload budget (MTU {VoiceMtu})."
			);
		}

		// ── Microphone events (audio mod) ─────────────────────────────────────

		/// <summary>The audio mod switched to another device: capture that one instead.</summary>
		private void OnMicrophoneCurrentChanged(EventData context) {
			var microphone = context.TryGet<IMicrophone>(0, out var mic) ? mic : null;
			var api = MicrophoneAPI;

			BindMicrophone(microphone ?? api?.Current ?? api?.Default);
		}

		/// <summary>The bound device disappeared: fall back to the mod's current one.</summary>
		private void OnMicrophoneRemoved(EventData context) {
			if (!context.TryGet<IMicrophone>(0, out var microphone) || microphone != _microphone)
				return;

			var api = MicrophoneAPI;
			BindMicrophone(api?.Current ?? api?.Default);
		}

		/// <summary>
		/// The captured device became silent (muted, or its volume reached 0): announce
		/// <see cref="SpeakMode.Muted"/> and lift it back to the chosen mode when it stops.
		/// </summary>
		private void OnMicrophoneMuteChanged(EventData context) {
			if (!context.TryGet<IMicrophone>(0, out var microphone) || microphone != _microphone)
				return;

			ApplyMicrophoneSilence();
		}

		/// <summary>Volume 0 captures nothing: it is a mute for every consumer of the voice state.</summary>
		private void OnMicrophoneVolumeChanged(EventData context) {
			if (!context.TryGet<IMicrophone>(0, out var microphone) || microphone != _microphone)
				return;

			ApplyMicrophoneSilence();
		}

		/// <summary>Announces whether the bound device is silent (<see cref="MicrophoneExtensions.IsSilent"/>).</summary>
		private void ApplyMicrophoneSilence() {
			if (_microphone == null)
				return;

			ApplySpeakState();
		}

		// ── Mute notifications ────────────────────────────────────────────────
		//
		// The private "you have been muted" relations are handled by the session
		// (<c>Session.Voice.cs</c>): the local player only carries its own voice state.

		// ── Emission ──────────────────────────────────────────────────────────

		private void SendVoicePacket(byte[] packet) {
			var room = Context.Context.Room;
			if (room == null)
				return;

			var flags = Speak switch {
				SpeakMode.Whisper => StreamLevelFlags.Whisper,
				SpeakMode.Broadcast => StreamLevelFlags.Broadcast,
				_ => StreamLevelFlags.Normal
			};

			room.Stream(StreamRequest.MakeSample(
					flags,
					packet,
					_pipeline.Index++,
					Time.time - _pipeline.StartedAt
				))
				.Forget();
		}

		private struct VoiceModeState {
			public SpeakMode Desired;
			public SpeakMode? Announced;
			public SpeakMode? Session;
			public int AllowedModes;
			public bool ForceMute;
		}

		private struct VoicePipeline {
			public OpusAudioSender Sender;
			public int Index;
			public float StartedAt;
		}
	}
}
