using System;
using Nox.CCK.Audio.Opus;
using Nox.Avatars;
using Nox.Avatars.Voice;
using Nox.CCK.Utils;
using Nox.Relay.Core.Types.Stream;
using Nox.Relay.Runtime;
using Nox.Relay.Runtime.Physicals;
using Nox.Relay.Runtime.Players;
using UnityEngine;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>
	/// Remote player voice receiver — relay → Opus decode → playback.
	/// Attaches the decoder and AudioSource output to the player's <see cref="RemotePhysical"/>,
	/// re-resolving on avatar changes.
	/// </summary>
	public class RemoteVoiceProvider : VoiceProvider {
		private RemotePhysical _physical;
		private GameObject _anchor;
		private IRuntimeAvatar _runtimeAvatar;

		private VoiceAudioSourceOutput _output;
		private OpusDecoder.OpusDecoderInstance _decoder;
		private VoiceJitter _jitter;
		private bool _isEffectivelyMuted;

		private static readonly FrameStopwatch CodecStopwatch = new();
		private const string CodecTimeOverrunMessage =
			"Opus codec took too long this frame. Reduce complexity or increase maxCodecMs.";

		public bool IsOutputMuted;
		public float MaxCodecMilliseconds = 50;
		public bool AllowMultipleCodecWarningsPerFrame;

		public RemoteVoiceProvider(RemotePlayer player) : base(player) { }

		public override void Initialize() {
			if (Started)
				return;

			var session = Player.Context?.Context;
			if (session?.Room == null)
				return;

			Room = session.Room;

			if (!Player.TryGetPhysical<RemotePhysical>(out var physical))
				return; // Physical not ready — retry on next voice frame / avatar change

			_physical = physical;
			_decoder = new OpusDecoder.OpusDecoderInstance(OpusConfig.SamplesPerSecond, 1);
			_jitter = new VoiceJitter();

			_physical.ActuallyDestroyed.AddListener(OnPhysicalDestroyed);
			_physical.OnAvatarSet.AddListener(OnAvatarSet);

			CreateOrMigrateOutput();
			BindPlayerEvents();

			Started = true;

			session.RegisterVoiceProvider(Player.Id, this);

			Logger.LogDebug($"[Session] Voice chat set up for remote player {Player.Id}", tag: nameof(RemoteVoiceProvider));
		}

		public override void Dispose() {
			Teardown();
		}

		/// <summary>Handle an incoming voice frame from the relay.</summary>
		public void ReceiveRelayFrame(StreamEvent voiceEvent) {
			if (!Started || _output == null)
				return;

			var mode = VoiceDistanceModeExtensions.FromLevelFlags(voiceEvent.LevelFlags);
			if (_output.DistanceMode != mode) {
				_output.DistanceMode = mode;
				_output.ApplySpatialSettings();
			}

			ReceiveFrame(
				voiceEvent.FrameIndex,
				voiceEvent.Timestamp > 0 ? voiceEvent.Timestamp : Time.timeAsDouble,
				Time.deltaTime,
				voiceEvent.Sample ?? ReadOnlySpan<byte>.Empty
			);
		}

		private void ReceiveFrame(int index, double timestamp, float additionalLatency, ReadOnlySpan<byte> data) {
			float targetLatency = (OpusConfig.SecondsPerFrame * VoiceConfig.OutputMinBufferFrames)
				+ Time.deltaTime + additionalLatency
				+ _jitter.Update(timestamp);

			if (_isEffectivelyMuted) {
				SetIsSpeaking(false);
				_output.ReceiveFrame(index, null, targetLatency);
				return;
			}

			if (data.Length == 0) {
				SetIsSpeaking(false);
				_output.ReceiveFrame(index, null, targetLatency);
				return;
			}

			SetIsSpeaking(true);

			if (IsOutputMuted) {
				_output.ReceiveFrame(index, null, targetLatency);
				return;
			}

			float[] samples = null;
			bool hasDecodedYet = _decoder.IsValid;
			CodecStopwatch.Start();
			try {
				samples = _decoder.Decode(data.ToArray(), OpusConfig.SamplesPerFrame);
			} catch (Exception ex) {
				Debug.LogWarning($"[RemoteVoiceProvider] Opus decode failed: {ex.Message}");
			}
			CodecStopwatch.Stop(MaxCodecMilliseconds, CodecTimeOverrunMessage,
				!hasDecodedYet, AllowMultipleCodecWarningsPerFrame);

			if (samples != null && samples.Length == OpusConfig.SamplesPerFrame)
				_output.ReceiveFrame(index, samples, targetLatency);
			else
				_output.ReceiveFrame(index, null, targetLatency);
		}

		private void SetIsSpeaking(bool value)
			=> Player.IsSpeaking = value;

		private void BindPlayerEvents() {
			Player.OnVolume.AddListener(OnPlayerVolumeChanged);
			Player.OnMute.AddListener(OnPlayerMuteChanged);
			ApplyPlayerVolume(Player.EffectiveVolume);
			_isEffectivelyMuted = Player.IsEffectivelyMuted;
		}

		private void UnbindPlayerEvents() {
			Player.OnVolume.RemoveListener(OnPlayerVolumeChanged);
			Player.OnMute.RemoveListener(OnPlayerMuteChanged);
		}

		private void OnPlayerVolumeChanged(float local, float effective)
			=> ApplyPlayerVolume(effective);

		private void ApplyPlayerVolume(float effective) {
			if (_output != null && _output.AudioSource != null)
				_output.AudioSource.volume = effective;
		}

		private void OnPlayerMuteChanged(bool local, bool effective)
			=> _isEffectivelyMuted = effective;

		private void OnAvatarSet(IRuntimeAvatar avatar) {
			_runtimeAvatar = avatar;
			CreateOrMigrateOutput();
		}

		private void OnPhysicalDestroyed()
			=> Teardown();

		private void Teardown() {
			UnbindPlayerEvents();

			_decoder?.Dispose();
			_decoder = null;
			_jitter?.Reset();
			_jitter = null;

			if (_anchor != null) {
				_anchor.Destroy();
				_anchor = null;
			}

			if (_physical != null) {
				_physical.ActuallyDestroyed.RemoveListener(OnPhysicalDestroyed);
				_physical.OnAvatarSet.RemoveListener(OnAvatarSet);
				_physical = null;
			}

			_output = null;

			Player.Context?.Context.UnregisterVoiceProvider(Player.Id);

			Started = false;
		}

		private void CreateOrMigrateOutput() {
			if (_physical == null)
				return;

			// Assign a unique mixer track (0..255) per player so each voice can be
			// individually attenuated/muted in the AudioMixer.
			var mixerGroup = Main.VoiceRegister?.GetTrack(Player.Id & 0xFF);

			var voiceModules = _runtimeAvatar?.Descriptor?.GetModules<IVoiceModule>();
			var avatarSource = (voiceModules?.Length > 0)
				? voiceModules[0].GetSource()
				: null;

			if (avatarSource != null) {
				// Avatar provides a voice AudioSource — destroy fallback anchor and migrate
				if (_anchor != null) {
					_anchor.Destroy();
					_anchor = null;
				}

				_output = _physical.gameObject.GetOrAddComponent<VoiceAudioSourceOutput>();
				_output.MixerGroup = mixerGroup;
				_output.SetSource(avatarSource);
				return;
			}

			// No avatar voice source — ensure fallback "Voice" anchor exists at local origin
			if (_anchor == null) {
				_anchor = new GameObject("Voice");
				_anchor.transform.SetParent(_physical.transform, false);
				_anchor.transform.localPosition = Vector3.zero;
				_anchor.transform.localRotation = Quaternion.identity;

				_output = _anchor.AddComponent<VoiceAudioSourceOutput>();
				_output.MixerGroup = mixerGroup;

				var oldOutput = _physical.gameObject.GetComponent<VoiceAudioSourceOutput>();
				if (oldOutput != null && oldOutput != _output)
					oldOutput.Destroy();
			}
		}
	}
}
