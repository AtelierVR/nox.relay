using System;
using System.Collections.Generic;
using System.Linq;
using Nox.Audio.Runtime;
using Nox.Relay.Runtime.Players;
using UnityEngine;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>
	/// Central voice chat component — MetaVoiceChat MetaVc equivalent.
	/// One instance per player. Handles encoding+sending (local) and
	/// receiving+decoding+playback (remote).
	/// <para>
	/// Setup: add this component + a VoiceMicInput + a VoiceAudioSourceOutput
	/// + a VoiceRelayProvider to the same GameObject.
	/// </para>
	/// </summary>
	public class VoiceChat : MonoBehaviour {
		private const string CodecTimeOverrunMessage =
			"Opus codec took too long this frame. Reduce complexity or increase maxCodecMs.";

		[Header("General")]
		public VoiceMicInput AudioInput;
		public VoiceAudioSourceOutput AudioOutput;
		public VoiceConfig Config;

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

		/// <summary>
		/// Link to the player for volume/mute control from the channel hierarchy.
		/// Set externally by the provider after initialization.
		/// </summary>
		public Player Player;

		/// <summary>Cached effective mute state, updated via events.</summary>
		private bool _isEffectivelyMuted;

		/// <summary>
		/// Subscribe to the player's volume/mute events and apply initial values.
		/// Call after <see cref="Player"/> is set.
		/// </summary>
		public void BindPlayerEvents() {
			if (Player == null) return;
			Player.OnVolume.AddListener(OnPlayerVolumeChanged);
			Player.OnMute.AddListener(OnPlayerMuteChanged);
			ApplyPlayerVolume(Player.EffectiveVolume);
			_isEffectivelyMuted = Player.IsEffectivelyMuted;
		}

		private void UnbindPlayerEvents() {
			if (Player == null) return;
			Player.OnVolume.RemoveListener(OnPlayerVolumeChanged);
			Player.OnMute.RemoveListener(OnPlayerMuteChanged);
		}

		private void OnPlayerVolumeChanged(float local, float effective)
			=> ApplyPlayerVolume(effective);

		private void ApplyPlayerVolume(float effective) {
			if (AudioOutput is VoiceAudioSourceOutput src && src.AudioSource != null)
				src.AudioSource.volume = effective;
		}

		private void OnPlayerMuteChanged(bool local, bool effective)
			=> _isEffectivelyMuted = effective;

		// ── Internal state ──
		private VoiceRelayProvider _netProvider;
		private bool _isLocalPlayer;
		private bool _started;

		private OpusEncoder.OpusEncoderInstance _encoder;
		private OpusDecoder.OpusDecoderInstance _decoder;
		private VoiceJitter _jitter;

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
		public void StartClient(VoiceRelayProvider netProvider, bool isLocalPlayer, int maxDataBytesPerPacket) {
			if (_started) return;

			// Ensure config is initialized (may not have run Awake yet)
			if (Config) Config.Init();

			_started = true;

			_netProvider = netProvider;
			_isLocalPlayer = isLocalPlayer;

			if (isLocalPlayer) {
				int maxBytes = Math.Min(maxDataBytesPerPacket, 1275);
				_encoder = new OpusEncoder.OpusEncoderInstance(
					VoiceConfig.SamplesPerSecond, 1, Config.Bitrate);

				AudioInput.OnFrameReady += SendFrame;
				AudioInput.StartLocalPlayer();
			}

			_decoder = new OpusDecoder.OpusDecoderInstance(VoiceConfig.SamplesPerSecond, 1);
			_jitter = new VoiceJitter(Config);
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

			bool isSpeaking = HasSignal(samples);
			IsSpeaking = isSpeaking;

			// Encode only when there is voice to send (skip silence / muted frames).
			byte[] encoded = null;
			if (isSpeaking && !IsDeafened && !IsInputMuted) {
				bool hasEncodedYet = _encoder.IsValid;
				CodecStopwatch.Start();
				encoded = _encoder.Encode(samples, Config.SamplesPerFrame);
				CodecStopwatch.Stop(MaxCodecMilliseconds, CodecTimeOverrunMessage,
					!hasEncodedYet, AllowMultipleCodecWarningsPerFrame);

				if (encoded == null) encoded = Array.Empty<byte>();
			}

			// Local echo (testing) always feeds the output to stay in sync.
			if (IsEchoEnabled) {
				if (encoded != null && encoded.Length > 0)
					ReceiveFrame(index, Timestamp, 0, encoded);
				else
					ReceiveFrame(index, Timestamp, 0, ReadOnlySpan<byte>.Empty);
			}

			// Bandwidth optimization: don't send packets for silence.
			if (encoded != null && encoded.Length > 0)
				_netProvider.RelayFrame(index, Timestamp, encoded);
		}

		private static bool HasSignal(float[] samples) {
			if (samples == null) return false;
			float sumSq = 0f;
			for (int i = 0; i < samples.Length; i++)
				sumSq += samples[i] * samples[i];
			return sumSq > 1e-6f;
		}

		// ── Receive (all players) ──

		public void ReceiveFrame(int index, double timestamp, float additionalLatency, ReadOnlySpan<byte> data) {
			float targetLatency = (Config.SecondsPerFrame * Config.OutputMinBufferFrames)
				+ Time.deltaTime + additionalLatency;

			if (!_isLocalPlayer) {
				float jitter = _jitter.Update(timestamp);
				targetLatency += jitter;
			}

			// If player is effectively muted via the channel hierarchy, skip processing
			if (!_isLocalPlayer && _isEffectivelyMuted) {
				SetIsSpeaking(false);
				AudioOutput.ReceiveFrame(index, null, targetLatency);
				return;
			}

			if (data.Length == 0) {
				SetIsSpeaking(false);
				AudioOutput.ReceiveFrame(index, null, targetLatency);
			} else {
				SetIsSpeaking(true);

				if (CannotSpeak && !ShouldLocalEcho) {
					AudioOutput.ReceiveFrame(index, null, targetLatency);
				} else {
					bool hasDecodedYet = _decoder.IsValid;
					CodecStopwatch.Start();
					float[] samples = null;
					try {
						samples = _decoder.Decode(data.ToArray(), Config.SamplesPerFrame);
					} catch (Exception ex) {
						Debug.LogWarning($"[VoiceChat] Opus decode failed: {ex.Message}");
					}
					CodecStopwatch.Stop(MaxCodecMilliseconds, CodecTimeOverrunMessage,
						!hasDecodedYet, AllowMultipleCodecWarningsPerFrame);

					if (samples != null && samples.Length == Config.SamplesPerFrame) {
						AudioOutput.ReceiveFrame(index, samples, targetLatency);
					} else {
						AudioOutput.ReceiveFrame(index, null, targetLatency);
					}
				}
			}
		}

		private void SetIsSpeaking(bool value) {
			if (_isLocalPlayer) return; // Already set in SendFrame
			IsSpeaking = value;
		}

		private void OnDestroy() {
			UnbindPlayerEvents();
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

	/// <summary>
	/// RMS jitter calculator — tracks network jitter to adjust output latency.
	/// </summary>
	public class VoiceJitter {
		private readonly double _timeWindow;
		private readonly int _meanOffsetWindow;

		private readonly System.Diagnostics.Stopwatch _stopwatch = new();
		private double LocalTimestamp => _stopwatch.Elapsed.TotalSeconds;

		private readonly Queue<Entry> _entries = new();
		private readonly Queue<double> _offsets = new();

		public VoiceJitter(VoiceConfig config) {
			_timeWindow = config.JitterTimeWindow;
			_meanOffsetWindow = config.JitterMeanOffsetWindow;
		}

		/// <summary>
		/// Feed a new packet and get the current RMS jitter.
		/// When sender timestamps are unavailable (timestamp=0), uses arrival-timing mode.
		/// </summary>
		public float Update(double timestamp) {
			if (!_stopwatch.IsRunning) {
				_stopwatch.Restart();
				return 0;
			}

			double localTimestamp = LocalTimestamp;
			double effectiveTimestamp = timestamp > 0 ? timestamp : localTimestamp;

			_entries.Enqueue(new Entry(effectiveTimestamp, localTimestamp));
			while (_entries.TryPeek(out var entry)) {
				if (entry.GetAge(localTimestamp) > _timeWindow)
					_entries.Dequeue();
				else
					break;
			}

			_offsets.Enqueue(localTimestamp - effectiveTimestamp);
			if (_offsets.Count > _meanOffsetWindow)
				_offsets.Dequeue();

			double meanOffset = _offsets.Average();

			if (_entries.Count > 1) {
				float SquareDeviation(Entry e) {
					double deviation = meanOffset + e.timestamp - e.localTimestamp;
					return (float)(deviation * deviation);
				}
				return Mathf.Sqrt(_entries.Average(SquareDeviation));
			}

			return 0;
		}

		public void Reset() {
			_stopwatch.Reset();
			_entries.Clear();
			_offsets.Clear();
		}

		private readonly struct Entry {
			public readonly double timestamp;
			public readonly double localTimestamp;

			public Entry(double timestamp, double localTimestamp) {
				this.timestamp = timestamp;
				this.localTimestamp = localTimestamp;
			}

			public float GetAge(double localTimestamp)
				=> (float)(localTimestamp - this.localTimestamp);
		}
	}
}
