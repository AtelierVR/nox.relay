using System;
using Nox.CCK.Microphone;
using Nox.Microphone.Players;
using Nox.Microphone.Runtime;
using Nox.Relay.Core.Types.Stream;
using Nox.Relay.Runtime.Players;
using UnityEngine;
using Room = Nox.Relay.Core.Rooms.Room;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>
	/// Lit le micro, encode en Opus et envoie sur le réseau.
	/// Pattern identique au Send() du test, adapté au ring buffer du micro.
	/// Le read cursor avance TOUJOURS pour rester synchro avec le live mic.
	/// </summary>
	public class VoiceSender {
		private readonly LocalPlayer   _player;
		private readonly VoiceManager  _manager;
		private readonly Room          _room;
		private readonly OpusEncoder.OpusEncoderInstance _encoder;

		private int     _lastPos;
		private IAudio  _lastAudio;
		private float[] _buf;
		private bool    _disposed;

		public uint   ChannelId { get; }
		public string Key       { get; }

		private int FrameSize => _manager.Config?.FrameSize ?? 960;

		public VoiceSender(LocalPlayer player, VoiceManager manager, uint channelId) {
			_player   = player;
			_manager  = manager;
			_room     = player.Context?.Context.Room;
			ChannelId = channelId;
			Key       = $"{player.Id}_{channelId:X8}";
			_buf      = new float[FrameSize];

			var cfg = manager.Config;
			_encoder = new OpusEncoder.OpusEncoderInstance(
				cfg?.SampleRate ?? 48000, 1, cfg?.Bitrate ?? 64000
			);
		}

		public void Update() {
			if (_disposed || _encoder == null || _room == null) return;

			var audio = _player.Audio;
			if (audio == null) return;

			int pos = audio.GetPosition();
			if (!ReferenceEquals(audio, _lastAudio)) {
				_lastAudio = audio;
				_lastPos   = pos;
				return;
			}

			var clip = audio.Clip;
			if (clip == null) return;

			int avail = pos >= _lastPos
				? pos - _lastPos
				: (clip.samples - _lastPos) + pos;

			if (avail < FrameSize) return;

			// ── Lit 1 frame (gère le wrap du ring buffer) ──
			int start = _lastPos % clip.samples;
			if (start + FrameSize <= clip.samples) {
				clip.GetData(_buf, start);
			} else {
				int first  = clip.samples - start;
				int second = FrameSize - first;
				var tmp = new float[first];
				clip.GetData(tmp, start);
				Array.Copy(tmp, 0, _buf, 0, first);
				var tail = new float[second];
				clip.GetData(tail, 0);
				Array.Copy(tail, 0, _buf, first, second);
			}

			// ⚠ Avance TOUJOURS le curseur de lecture, même si on n'envoie pas
			_lastPos = (start + FrameSize) % clip.samples;

			// ── Volume ──
			float vol = MicrophoneSettings.Volume;
			if (Math.Abs(vol - 1f) > 0.001f)
				for (int i = 0; i < FrameSize; i++) _buf[i] *= vol;

			// ── VAD ──
			if (!HasVoiceActivity(_buf, FrameSize)) return;

			// ── Encode + send (comme le Send() du test) ──
			try {
				byte[] enc = _encoder.Encode(_buf, FrameSize);
				if (enc != null && enc.Length > 0) {
					_ = _room.Stream(StreamRequest.MakeSample(
						ChannelId, SpeakToWire(GetSpeakMode()), enc));
					_manager.GetOrCreatePermissions(_player).Speaking = true;
				}
			} catch (Exception ex) {
				Debug.LogWarning($"[VoiceSender] Error: {ex.Message}");
			}
		}

		private SpeakMode GetSpeakMode()
			=> _manager.GetOrCreatePermissions(_player).Speak;

		private static byte SpeakToWire(SpeakMode m) => m switch {
			SpeakMode.Whisper   => 0,
			SpeakMode.Normal    => 1,
			SpeakMode.Loud      => 1,
			SpeakMode.Broadcast => 2,
			_                   => 1,
		};

		private static bool HasVoiceActivity(float[] samples, int count) {
			float sum = 0f;
			for (int i = 0; i < count && i < samples.Length; i++)
				sum += samples[i] * samples[i];
			return Mathf.Sqrt(sum / count) > MicrophoneSettings.ActivationThreshold;
		}

		public void Dispose() {
			if (_disposed) return;
			_disposed = true;
			_encoder?.Dispose();
		}
	}
}
