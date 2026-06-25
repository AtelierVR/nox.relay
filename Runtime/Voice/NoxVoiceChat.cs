using System;
using Nox.Microphone.Runtime;
using UnityEngine;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>
	/// Central voice chat component — MetaVoiceChat MetaVc equivalent.
	/// One instance per player. Handles encoding+sending (local) and
	/// receiving+decoding+playback (remote).
	/// <para>
	/// Setup: add this component + a NoxVoiceInput + a NoxVoiceOutput
	/// + a NoxVoiceRelayProvider to the same GameObject.
	/// </para>
	/// </summary>
	public class NoxVoiceChat : MonoBehaviour {
		private const string CodecTimeOverrunMessage =
			"Opus codec took too long this frame. Reduce complexity or increase maxCodecMs.";

		[Header("General")]
		public NoxVoiceInput AudioInput;
		public NoxVoiceOutput AudioOutput;
		public NoxVoiceConfig Config;

		[Header("Testing")]
		[Tooltip("Plays back the local player's own voice.")]
		public bool IsEchoEnabled;
		[Tooltip("Overrides audio input with a 200 Hz sine wave for testing.")]
		public bool IsSineOverrideEnabled;
		[Tooltip("Max milliseconds allowed for Opus codec per frame before warning.")]
		[Range(0, 100)]
		public float MaxCodecMilliseconds = 50;
		[Tooltip("Allow multiple codec time overrun warnings per frame.")]
		public bool AllowMultipleCodecWarningsPerFrame;

		[Header("State")]
		[Tooltip("Local player: don't hear anyone else.")]
		public bool IsDeafened;
		[Tooltip("Local player: don't let anyone hear me.")]
		public bool IsInputMuted;
		[Tooltip("Remote player: don't hear this player.")]
		public bool IsOutputMuted;
		[Tooltip("This player is currently speaking.")]
		public bool IsSpeaking { get; private set; }

		// ── Internal state ──
		private INoxVoiceNetProvider _netProvider;
		private bool _isLocalPlayer;
		private bool _started;

		private OpusEncoder.OpusEncoderInstance _encoder;
		private OpusDecoder.OpusDecoderInstance _decoder;
		private NoxVoiceJitter _jitter;

		private readonly System.Diagnostics.Stopwatch _stopwatch = new();
		private double Timestamp => _stopwatch.Elapsed.TotalSeconds;

		private bool CannotSpeak => _netProvider?.IsLocalPlayerDeafened ?? false || IsOutputMuted;
		private bool ShouldLocalEcho => _isLocalPlayer && IsEchoEnabled;

		private static readonly FrameStopwatch CodecStopwatch = new();

		// ── Unity Lifecycle ──

		private void Awake() {
			if (Config != null)
				Config.Init();
			CodecStopwatch.Reset();
		}

		/// <summary>
		/// Called by the net provider when the network is ready.
		/// </summary>
		public void StartClient(INoxVoiceNetProvider netProvider, bool isLocalPlayer, int maxDataBytesPerPacket) {
			if (_started) return;

			// Ensure config is initialized (may not have run Awake yet)
			if (Config != null) Config.Init();

			_started = true;

			_netProvider = netProvider;
			_isLocalPlayer = isLocalPlayer;

			if (isLocalPlayer) {
				int maxBytes = Math.Min(maxDataBytesPerPacket, 1275);
				_encoder = new OpusEncoder.OpusEncoderInstance(
					NoxVoiceConfig.SamplesPerSecond, 1, Config.Bitrate);

				AudioInput.OnFrameReady += SendFrame;
				AudioInput.StartLocalPlayer();
			}

			_decoder = new OpusDecoder.OpusDecoderInstance(NoxVoiceConfig.SamplesPerSecond, 1);
			_jitter = new NoxVoiceJitter(Config);
			_stopwatch.Start();
		}

		public void StopClient() {
			if (!_started) return;
			_started = false;

			if (_isLocalPlayer) {
				_encoder?.Dispose();
				AudioInput.OnFrameReady -= SendFrame;
			}

			_decoder?.Dispose();
			_jitter?.Reset();
		}

		// ── Send (local player) ──

		private void SendFrame(int index, float[] samples) {
			if (!_isLocalPlayer) return;

			// ── Test sine override ──
			if (samples != null && IsSineOverrideEnabled) {
				const float Amplitude = 0.2f;
				float multiplier = Mathf.PI * (1.0f / 40.0f); // 200 Hz
				for (int i = 0; i < samples.Length; i++)
					samples[i] = Amplitude * Mathf.Sin(i * multiplier);
			}

			bool isSpeaking = samples != null;
			IsSpeaking = isSpeaking;

			bool shouldRelayEmpty = IsEchoEnabled
				? !isSpeaking
				: !isSpeaking || IsDeafened || IsInputMuted;

			if (shouldRelayEmpty) {
				if (IsEchoEnabled)
					ReceiveFrame(index, Timestamp, 0, ReadOnlySpan<byte>.Empty);
				_netProvider.RelayFrame(index, Timestamp, ReadOnlySpan<byte>.Empty);
			} else {
				bool hasEncodedYet = _encoder.IsValid;
				CodecStopwatch.Start();
				var data = _encoder.Encode(samples, Config.SamplesPerFrame);
				CodecStopwatch.Stop(MaxCodecMilliseconds, CodecTimeOverrunMessage,
					!hasEncodedYet, AllowMultipleCodecWarningsPerFrame);

				if (data == null) data = Array.Empty<byte>();

				if (IsEchoEnabled)
					ReceiveFrame(index, Timestamp, 0, data);
				else
					ReceiveFrame(index, Timestamp, 0, ReadOnlySpan<byte>.Empty);

				if (IsDeafened || IsInputMuted)
					_netProvider.RelayFrame(index, Timestamp, ReadOnlySpan<byte>.Empty);
				else
					_netProvider.RelayFrame(index, Timestamp, data);
			}
		}

		// ── Receive (all players) ──

		public void ReceiveFrame(int index, double timestamp, float additionalLatency, ReadOnlySpan<byte> data) {
			float targetLatency = (Config.SecondsPerFrame * Config.OutputMinBufferFrames)
				+ Time.deltaTime + additionalLatency;

			if (!_isLocalPlayer) {
				float jitter = _jitter.Update(timestamp);
				targetLatency += jitter;
			}

			if (data.Length == 0) {
				SetIsSpeaking(false);
				AudioOutput.ReceiveAndFilterFrame(index, null, targetLatency);
			} else {
				SetIsSpeaking(true);

				if (CannotSpeak && !ShouldLocalEcho) {
					AudioOutput.ReceiveAndFilterFrame(index, null, targetLatency);
				} else {
					bool hasDecodedYet = _decoder.IsValid;
					CodecStopwatch.Start();
					float[] samples = null;
					try {
						samples = _decoder.Decode(data.ToArray(), Config.SamplesPerFrame);
					} catch (Exception ex) {
						Debug.LogWarning($"[NoxVoiceChat] Opus decode failed: {ex.Message}");
					}
					CodecStopwatch.Stop(MaxCodecMilliseconds, CodecTimeOverrunMessage,
						!hasDecodedYet, AllowMultipleCodecWarningsPerFrame);

					if (samples != null && samples.Length == Config.SamplesPerFrame) {
						AudioOutput.ReceiveAndFilterFrame(index, samples, targetLatency);
					} else {
						AudioOutput.ReceiveAndFilterFrame(index, null, targetLatency);
					}
				}
			}
		}

		private void SetIsSpeaking(bool value) {
			if (_isLocalPlayer) return; // Already set in SendFrame
			IsSpeaking = value;
		}

		private void OnDestroy() {
			StopClient();
		}
	}

	/// <summary>
	/// Simple frame stopwatch for tracking codec time across calls.
	/// </summary>
	internal class FrameStopwatch {
		private readonly System.Diagnostics.Stopwatch _sw = new();

		public void Start() => _sw.Restart();
		public void Stop(float maxMs, string message, bool isFirstFrame, bool allowMultiple) {
			_sw.Stop();
			if ((isFirstFrame || allowMultiple) && _sw.Elapsed.TotalMilliseconds > maxMs)
				Debug.LogWarning(message);
		}
		public void Reset() => _sw.Reset();
	}
}
