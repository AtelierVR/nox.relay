using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using StirlingLabs.MsQuic;
using StirlingLabs.MsQuic.Bindings;
using UnityEngine.Events;
using Buffer = Nox.CCK.Utils.Buffer;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Relay.Core.Connectors {
	public class QuicConnector : IConnector {
		public const string PROTOCOL_NAME = "quic";
		private const string AlpnToken = "relay";
		private const int ConnectTimeoutMs = 10_000;

		private readonly QuicRegistration _registration;
		private readonly QuicClientConfiguration _config;

		private QuicClientConnection _connection;
		// Stores the TCS for the current Connect() call; shared with named callbacks
		// so no lambda captures are needed (IL2CPP AOT safety).
		private TaskCompletionSource<bool> _connectTcs;
		// Streams opened for outgoing requests — tracked so we can ensure they are
		// fully shut down before the registration is disposed. (Prevents the
		// MsQuicClose crash where a native worker thread is still delivering a
		// DataReceived callback while the library tears down.)
		private readonly ConcurrentBag<QuicStream> _openStreams = new();
		private IPEndPoint _endPoint;
		private volatile bool _isConnected;
		private bool _disposed;

		// ── Constructor ─────────────────────────────────────────────────────

		public QuicConnector() {
			_registration = new QuicRegistration("relay-client");
			_config       = new QuicClientConfiguration(_registration, AlpnToken);
			// NO_CERTIFICATE_VALIDATION: relay servers use self-signed certs; skip native
			// certificate validation entirely so the managed DefaultManagedCallback never
			// tries to call SignedCms.Decode(ReadOnlySpan<byte>), which is absent from
			// Unity's embedded Mono runtime in standalone builds.
			_config.ConfigureCredentials(QUIC_CREDENTIAL_FLAGS.NO_CERTIFICATE_VALIDATION);
		}

		// ── IQuicRelayClient ────────────────────────────────────────────────

		public string Protocol
			=> PROTOCOL_NAME;

		public bool IsConnected
			=> _isConnected;

		public EndPoint EndPoint
			=> _endPoint;

		public ushort Mtu {
			get => _connection?.MaxSendLength ?? 0;
			set { }
		}

		public UnityEvent<Buffer> OnReceived { get; } = new();
		public UnityEvent<bool> OnConnected { get; } = new();
		public UnityEvent<string> OnDisconnected { get; } = new();

		// ── Connect ─────────────────────────────────────────────────────────

		public async UniTask<bool> Connect(string address, ushort port) {
			if (!PlayerLoopHelper.IsMainThread)
				throw new InvalidOperationException($"Send must be called from the Unity main thread (your current {Thread.CurrentThread.ManagedThreadId} thread is not allowed to call Send).");

			if (_disposed)
				throw new ObjectDisposedException(nameof(QuicConnector));

			// Tear down any previous connection (registration & config are reused)
			await DropConnection().ConfigureAwait(false);

			_connection = new QuicClientConnection(_config);

			// Set provisional endpoint from the call parameters so EndPoint is
			// available as soon as Connect() completes, regardless of whether
			// the MsQuic RemoteEndPoint is populated at Connected-event time.
			if (IPAddress.TryParse(address, out var parsedIp))
				_endPoint = new IPEndPoint(parsedIp, port);

			// NO_CERTIFICATE_VALIDATION is already set in the config — no cert callback needed.
			// (Adding a CertificateReceived lambda here would create a closure IL2CPP cannot
			// reliably AOT-compile during the TLS handshake, which was the root cause of the
			// standalone build timeout.)

			_connectTcs = new TaskCompletionSource<bool>();

			// Named instance methods instead of lambdas: IL2CPP generates correct AOT
			// trampolines for method-group delegates, whereas lambda closures that capture
			// variables can silently fail inside native MsQuic callbacks in IL2CPP builds.
			_connection.Connected            += HandleConnected;
			_connection.ConnectionShutdown   += HandleConnectionShutdown;
			_connection.IncomingStream       += HandleIncomingStream;
			_connection.DatagramReceived     += HandleDatagramReceived;

			try {
				// Start is fire-and-forget; Connected / ConnectionShutdown drive the TCS
				_connection.Start(address, port);

				var timeout  = Task.Delay(ConnectTimeoutMs);
				var finished = await Task.WhenAny(_connectTcs.Task, timeout).ConfigureAwait(false);

				if (finished == timeout) {
					OnConnected?.Invoke(false);
					return false;
				}

				return await _connectTcs.Task.ConfigureAwait(false);
			} catch (Exception ex) {
				Logger.LogError($"[QuicConnector] Exception during Connect to {address}:{port} — {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
				OnConnected?.Invoke(false);
				return false;
			} finally {
				await UniTask.SwitchToMainThread();
			}
		}

		// ── QuicClientConnection callbacks ──────────────────────────────────
		// Named instance methods instead of lambdas: IL2CPP generates correct AOT
		// trampolines for method-group delegates. Lambda closures that capture local
		// variables are compiled as compiler-generated closure classes whose AOT
		// trampolines IL2CPP cannot always guarantee during native MsQuic callbacks.

		private void HandleConnected(QuicClientConnection conn) {
			_isConnected = true;
			if (conn.RemoteEndPoint != null)
				_endPoint = conn.RemoteEndPoint;
			_connectTcs?.TrySetResult(true);
			OnConnected?.Invoke(true);
		}

		private void HandleConnectionShutdown(QuicPeerConnection _, ulong errorCode, bool initiatedByTransport, bool initiatedByPeer) {
			_isConnected = false;
			var reason = initiatedByPeer
				? "Server closed the connection"
				: initiatedByTransport
					? $"Transport error (code {errorCode})"
					: $"Application closed the connection (code {errorCode})";
			Logger.LogWarning($"[QuicConnector] ConnectionShutdown: {reason} (byPeer={initiatedByPeer}, byTransport={initiatedByTransport}, code={errorCode})");
			_connectTcs?.TrySetResult(false);
			OnDisconnected?.Invoke(reason);
		}

		private void HandleIncomingStream(QuicPeerConnection _, QuicStream stream)
			=> AttachStreamHandlers(stream);

		private void HandleDatagramReceived(QuicPeerConnection _, ReadOnlySpan<byte> span) {
			// Copy immediately — the native span is only valid for the duration of this callback.
			var data = span.ToArray();
			// Dispatch processing to the Unity main thread so OnReceived subscribers
			// can safely interact with Unity objects.
			UniTask.Post(() => {
				var buff = new Buffer();
				buff.Write(data);
				buff.Start();
				OnReceived?.Invoke(buff);
			});
		}

		// ── Stream helpers ───────────────────────────────────────────────────

		/// <summary>
		/// Opens a fresh bidi stream and attaches receive handlers so the relay's
		/// response fires <see cref="OnReceived"/>.
		/// Does NOT call Start() — the stream is started atomically on the first
		/// SendAsync via <see cref="QUIC_SEND_FLAGS.START"/>, which avoids the
		/// QUIC_STATUS_INVALID_STATE that occurs when Start() (async) and SendAsync
		/// are issued back-to-back before the start acknowledgement arrives.
		/// </summary>
		private QuicStream OpenRequestStream() {
			var stream = _connection.OpenStream();
			_openStreams.Add(stream);
			AttachStreamHandlers(stream); // subscribe to relay response before any send
			return stream;
		}

		private void AttachStreamHandlers(QuicStream stream) {
			stream.DataReceived += s => {
				var available = (int)s.DataAvailable;
				if (available <= 0)
					return;

				var buf  = new byte[ available ];
				var read = s.Receive(new Span<byte>(buf));
				if (read <= 0)
					return;

				// Split concatenated messages by length prefix.
				// After a freeze, multiple relay messages may accumulate
				// on the QUIC stream.  Each message is prefixed with a
				// 2-byte big-endian uint16 total-length (header + payload).
				int offset = 0;
				const int LengthFieldSize = 2;
				const int MinMessageSize = 5; // length(2) + state(2) + type(1)

				while (offset + LengthFieldSize <= read) {
					// Big-endian u16 length prefix
					int msgLen = (buf[offset] << 8) | buf[offset + 1];

					if (msgLen < MinMessageSize || offset + msgLen > read) {
						// Truncated or corrupt — stop processing this chunk.
						// Remaining bytes will be picked up on the next
						// DataReceived together with new data.
						if (msgLen >= MinMessageSize && offset + msgLen > read)
							Logger.LogWarning(
							$"Partial message at stream end " +
							$"(need {msgLen}, have {read - offset} bytes left)",
							tag: nameof(QuicConnector));
					else if (msgLen < MinMessageSize && msgLen > 0)
						Logger.LogWarning(
							$"Corrupt length prefix {msgLen} " +
							$"at offset {offset} — skipping {read - offset} bytes",
							tag: nameof(QuicConnector));
						break;
					}

					// Copy this individual message and dispatch
					var msgBytes = new byte[msgLen];
					Array.Copy(buf, offset, msgBytes, 0, msgLen);
					offset += msgLen;

					UniTask.Post(() => {
						var buff = new Buffer();
						buff.Write(msgBytes);
						buff.Start();
						OnReceived?.Invoke(buff);
					});
				}
			};
			// When the relay closes its send side the stream reaches SHUTDOWN_COMPLETE.
			// Close the stream here so the native handle is returned to MsQuic
			// before the registration is torn down (prevents the MsQuicClose crash).
			stream.ShutdownComplete += (s, connectionShutdown, appCloseInProgress) => {
				if (!connectionShutdown) // still alive when conn is being shut down — conn.Dispose handles it
					try { s.Dispose(); } catch {
						// ignored
					}
				_openStreams.TryTake(out _); // keep the bag small
			};
		}

		// ── Send ─────────────────────────────────────────────────────────────

		public async UniTask<bool> Send(Buffer buffer, SendType type) {
			if (!PlayerLoopHelper.IsMainThread)
				throw new InvalidOperationException($"Send must be called from the Unity main thread (your current {Thread.CurrentThread.ManagedThreadId} thread is not allowed to call Send).");

			if (_connection == null || !_isConnected)
				return false;

			try {
				switch (type) {
					case SendType.Datagram:
						// SendDatagram expects Memory<byte>; copy via byte[] (implicit cast)
						_connection.SendDatagram(buffer.ToArray());
						return true;

					case SendType.Auto:
					case SendType.Stream:
						// Open a fresh bidi stream per request — the relay reads exactly one
						// framed message per bidi stream then closes its send side.
						// START atomically starts the stream on the first send (avoids the
						// INVALID_STATE that arises from a separate async Start() call).
						// FIN closes our send half so the relay knows the request is complete.
						var stream = OpenRequestStream();
						await stream.SendAsync(buffer, QUIC_SEND_FLAGS.START | QUIC_SEND_FLAGS.FIN).ConfigureAwait(false);
						return true;

					default:
						throw new ArgumentOutOfRangeException(nameof(type));
				}
			} catch (Exception) {
				return false;
			} finally {
				await UniTask.SwitchToMainThread();
			}
		}

		// ── Close / Dispose ──────────────────────────────────────────────────

		public async UniTask Close() {
			if (!PlayerLoopHelper.IsMainThread)
				throw new InvalidOperationException($"Send must be called from the Unity main thread (your current {Thread.CurrentThread.ManagedThreadId} thread is not allowed to call Send).");

			_isConnected = false;
			await DropConnection().ConfigureAwait(false);

			await UniTask.SwitchToMainThread();
		}

		/// <summary>
		/// Tears down only the current <see cref="QuicClientConnection"/>.
		/// The <see cref="QuicRegistration"/> and <see cref="QuicClientConfiguration"/>
		/// remain alive and are reused on the next <see cref="Connect"/> call.
		/// </summary>
		/// <remarks>
		/// Awaits <c>ConnectionShutdownComplete</c> before disposing the connection
		/// handle. This is essential: MsQuic fires <c>DataReceived</c> callbacks
		/// directly on its internal worker threads; calling
		/// <see cref="QuicRegistration.Dispose"/> (→ <c>MsQuicClose</c>) before
		/// those callbacks return crashes the process.
		/// </remarks>
		private async Task DropConnection() {
			var conn = _connection;
			_connection = null;
			if (conn == null)
				return;

			// Latch the native SHUTDOWN_COMPLETE — at that point MsQuic guarantees
			// no further callbacks will fire for this connection or its streams.
			var shutdownDone = new TaskCompletionSource<bool>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			conn.ConnectionShutdownComplete += (_, _, _, _) => shutdownDone.TrySetResult(true);

			try { conn.Shutdown(); } catch (Exception) { shutdownDone.TrySetResult(true); }

			// 3-second safety cap so Dispose() never hangs indefinitely.
			await Task.WhenAny(shutdownDone.Task, Task.Delay(3_000)).ConfigureAwait(false);

			// Drain any tracked streams whose ShutdownComplete did not fire
			// (e.g. timed-out paths or streams opened but never started).
			while (_openStreams.TryTake(out var s))
				try { s.Dispose(); } catch { }

			try { conn.Dispose(); } catch (Exception) { }
		}

		public async UniTask Dispose() {
			if (!PlayerLoopHelper.IsMainThread)
				throw new InvalidOperationException($"Send must be called from the Unity main thread (your current {Thread.CurrentThread.ManagedThreadId} thread is not allowed to call Send).");

			if (_disposed)
				return;
			
			_disposed    = true;
			_isConnected = false;

			// Await full native teardown before disposing config/registration so
			// MsQuicClose is never called while MsQuic worker threads are still
			// delivering DataReceived callbacks (cause of the msquic-openssl crash).
			await DropConnection();

			try { _config.Dispose(); } catch {
				// ignored
			}

			try { _registration.Dispose(); } catch {
				// ignored
			}

			GC.SuppressFinalize(this);
			await UniTask.SwitchToMainThread();
		}
	}
}