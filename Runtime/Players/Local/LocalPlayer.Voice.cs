using System;
using Cysharp.Threading.Tasks;
using Nox.Audio;
using Nox.Audio.Players;
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

		private OpusAudioSender _voiceSender;

		/// <summary>Device currently capturing for this player, or null while none is bound.</summary>
		private IMicrophone _microphone;

		/// <summary>Subscriptions to the audio mod's microphone events, while a device is bound.</summary>
		private EventSubscription[] _voiceEvents = Array.Empty<EventSubscription>();

		/// <summary>Mode announced while the device is live; a muted device announces <see cref="SpeakMode.Muted"/>.</summary>
		private SpeakMode _speakWhenUnmuted = SpeakMode.Normal;

		/// <summary>Set once the "no microphone" warning was emitted, to keep the per-frame retry quiet.</summary>
		private bool _microphoneWarned;

		private int _voiceFrameIndex;
		private float _voiceStartedAt;

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
			if (_voiceSender != null)
				return;

			BindMicrophone();

			StartSender();
		}

		/// <summary>Stops the capture and releases the emission pipeline. Safe to call when it never started.</summary>
		internal void StopVoice() {
			_voiceSender?.Dispose();
			_voiceSender = null;

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
			if (_voiceSender == null) {
				StartVoice();
				if (_voiceSender == null)
					return;
			} else {
				// The transport payload moves with the path MTU (QUIC re-negotiates it live): the emitter
				// re-sizes itself in place, and ignores a budget that did not change.
				_voiceSender.MaxPayloadSize = VoicePayloadBudget;
			}

			// Muted: keep the pipeline alive but emit nothing.
			if (Speak == SpeakMode.Muted) {
				_voiceSender.Pause();
				return;
			}

			// Resume() re-arms the read cursor: calling it while already running would reset that
			// cursor every frame, and Tick() would never see a single new sample.
			if (!_voiceSender.IsRunning)
				_voiceSender.Resume();

			_voiceSender.Tick();
		}

		// ── Microphone capture ────────────────────────────────────────────────

		/// <summary>Binds the audio mod's current microphone, falling back to its default one.</summary>
		private void BindMicrophone() {
			var api        = MicrophoneAPI;
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

			if (microphone == null)
				return;

			_microphone = microphone;

			var clip = microphone.Start(MicrophoneOwner);
			Audio    = clip ? new CapturedMicrophone(clip, microphone) : null;

			_voiceEvents = new[] {
				Main.CoreAPI.EventAPI.Subscribe("audio.microphone.current_changed", OnMicrophoneCurrentChanged),
				Main.CoreAPI.EventAPI.Subscribe("audio.microphone.mute_changed", OnMicrophoneMuteChanged),
				Main.CoreAPI.EventAPI.Subscribe("audio.microphone.removed", OnMicrophoneRemoved)
			};

			// The emitter reads the capture it was built with, and a device switch replaced it: rebuild.
			_voiceSender?.Dispose();
			_voiceSender = null;
			StartSender();

			// The new device may already be muted: announce its state right away.
			if (Speak != SpeakMode.Muted)
				_speakWhenUnmuted = Speak;
			Speak = microphone.IsMuted ? SpeakMode.Muted : _speakWhenUnmuted;
		}

		/// <summary>Stops the bound device, unsubscribes its events and forgets the capture.</summary>
		private void UnbindMicrophone() {
			if (_voiceEvents.Length > 0) {
				foreach (var subscription in _voiceEvents)
					Main.CoreAPI.EventAPI.Unsubscribe(subscription);
				_voiceEvents = Array.Empty<EventSubscription>();
			}

			_microphone?.Stop(MicrophoneOwner);
			_microphone = null;
			Audio       = null;
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
			if (_voiceSender != null || Audio?.Clip == null)
				return;

			var settings = OpusAudioSender.Settings.FromBudget(VoicePayloadBudget);

			_voiceFrameIndex = 0;
			_voiceStartedAt  = Time.time;
			_voiceSender     = new OpusAudioSender(Audio, SendVoicePacket, settings);

			Logger.LogDebug(
				$"Voice emitter: {settings.Bitrate / 1000} kbps, {settings.FrameDurationMs} ms frames, "
				+ $"{settings.MaxPayloadSize} B payload budget (MTU {VoiceMtu})."
			);
		}

		// ── Microphone events (audio mod) ─────────────────────────────────────

		/// <summary>The audio mod switched to another device: capture that one instead.</summary>
		private void OnMicrophoneCurrentChanged(EventData context) {
			var microphone = context.TryGet<IMicrophone>(0, out var mic) ? mic : null;
			var api        = MicrophoneAPI;

			BindMicrophone(microphone ?? api?.Current ?? api?.Default);
		}

		/// <summary>The bound device disappeared: fall back to the mod's current one.</summary>
		private void OnMicrophoneRemoved(EventData context) {
			if (!context.TryGet<IMicrophone>(0, out var microphone) || microphone != _microphone)
				return;

			var api = MicrophoneAPI;
			BindMicrophone(api?.Current ?? api?.Default);
		}

		/// <summary>Mute is announced as <see cref="SpeakMode.Muted"/>, and lifted back to the chosen mode.</summary>
		private void OnMicrophoneMuteChanged(EventData context) {
			if (!context.TryGet<IMicrophone>(0, out var microphone) || microphone != _microphone)
				return;

			var muted = context.TryGet<bool>(1, out var value) && value;

			if (Speak != SpeakMode.Muted)
				_speakWhenUnmuted = Speak;
			Speak = muted ? SpeakMode.Muted : _speakWhenUnmuted;
		}

		// ── Emission ──────────────────────────────────────────────────────────

		private void SendVoicePacket(byte[] packet) {
			var room = Context.Context.Room;
			if (room == null)
				return;

			var flags = Speak switch {
				SpeakMode.Whisper   => StreamLevelFlags.Whisper,
				SpeakMode.Broadcast => StreamLevelFlags.Broadcast,
				_                   => StreamLevelFlags.Normal
			};

			room.Stream(StreamRequest.MakeSample(
					flags,
					packet,
					_voiceFrameIndex++,
					Time.time - _voiceStartedAt
				))
				.Forget();
		}
	}
}
