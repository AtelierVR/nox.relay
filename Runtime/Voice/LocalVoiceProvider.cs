using System;
using Nox.CCK.Audio.Opus;
using Nox.Relay.Core.Types.Stream;
using Nox.Relay.Runtime.Players;
using UnityEngine;
using Logger = Nox.CCK.Utils.Logger;
using Object = UnityEngine.Object;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>
	/// Local player voice sender — microphone → Opus encode → relay.
	/// Hosts the microphone and encoder on a dedicated persistent GameObject.
	/// </summary>
	public class LocalVoiceProvider : VoiceProvider {
		private GameObject _root;
		private VoiceMicInput _mic;
		private OpusEncoder.OpusEncoderInstance _encoder;
		private int _maxDataBytes;

		private readonly System.Diagnostics.Stopwatch _stopwatch = new();
		private double Timestamp => _stopwatch.Elapsed.TotalSeconds;

		private static readonly FrameStopwatch CodecStopwatch = new();
		private const string CodecTimeOverrunMessage =
			"Opus codec took too long this frame. Reduce complexity or increase maxCodecMs.";

		public bool IsDeafened;
		public bool IsInputMuted;
		public float MaxCodecMilliseconds = 50;
		public bool AllowMultipleCodecWarningsPerFrame;

		public LocalVoiceProvider(LocalPlayer player) : base(player) { }

		public override void Initialize() {
			if (Started)
				return;

			var session = Player.Context?.Context;
			if (session?.Room == null)
				return;

			Room = session.Room;

			_root = new GameObject($"LocalVoice_{Player.Id}");
			Object.DontDestroyOnLoad(_root);

			_mic = _root.AddComponent<VoiceMicInput>();

			_maxDataBytes = Math.Min(MaxDataBytesPerPacket, OpusEncoder.MaxPacketSize);

			// Adapt the target bitrate to the connection MTU: fill the available
			// packet budget so quality scales with the link and packets never
			// fragment. Config bitrate is an optional ceiling (0 = auto).
			int mtuBitrate = _maxDataBytes * OpusConfig.FramesPerSecond * 8;
			int bitrate = Math.Min(mtuBitrate, OpusEncoder.MaxBitrate);
			if (OpusConfig.Bitrate > 0)
				bitrate = Math.Min(bitrate, OpusConfig.Bitrate);

			_encoder = new OpusEncoder.OpusEncoderInstance(
				OpusConfig.SamplesPerSecond, 1, bitrate,
				OpusConfig.Complexity, OpusConfig.SignalType);

			_mic.OnFrameReady += SendFrame;
			_mic.StartLocalPlayer();

			_stopwatch.Start();
			Started = true;

			session.RegisterVoiceProvider(Player.Id, this);

			Logger.LogDebug($"[Session] Voice chat set up for local player {Player.Id}", tag: nameof(LocalVoiceProvider));
		}

		public override void Dispose() {
			if (_mic != null) {
				_mic.OnFrameReady -= SendFrame;
				_mic = null;
			}

			_encoder?.Dispose();
			_encoder = null;

			Player.Context?.Context.UnregisterVoiceProvider(Player.Id);

			if (_root != null) {
				Object.Destroy(_root);
				_root = null;
			}

			Started = false;
		}

		/// <summary>Relay an encoded voice frame to all other players.</summary>
		public void RelayFrame(int index, double timestamp, ReadOnlySpan<byte> data) {
			if (Room == null || !Started)
				return;

			byte[] sample = data.IsEmpty ? Array.Empty<byte>() : data.ToArray();
			var flags = VoiceConfig.DefaultDistanceMode.ToLevelFlags();
			_ = Room.Stream(StreamRequest.MakeSample(flags, sample, index, timestamp));
		}

		private void SendFrame(int index, float[] samples) {
			bool isSpeaking = HasSignal(samples);
			Player.IsSpeaking = isSpeaking;

			if (!isSpeaking || IsDeafened || IsInputMuted)
				return;

			bool hasEncodedYet = _encoder.IsValid;
			CodecStopwatch.Start();
			byte[] encoded = _encoder.Encode(samples, OpusConfig.SamplesPerFrame, _maxDataBytes);
			CodecStopwatch.Stop(MaxCodecMilliseconds, CodecTimeOverrunMessage,
				!hasEncodedYet, AllowMultipleCodecWarningsPerFrame);

			// Bandwidth optimization: don't send packets for silence.
			if (encoded != null && encoded.Length > 0)
				RelayFrame(index, Timestamp, encoded);
		}

		private static bool HasSignal(float[] samples) {
			if (samples == null) return false;
			float sumSq = 0f;
			for (int i = 0; i < samples.Length; i++)
				sumSq += samples[i] * samples[i];
			return sumSq > 1e-6f;
		}
	}
}
