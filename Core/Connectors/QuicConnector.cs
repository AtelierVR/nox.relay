using System;
using System.Collections.Concurrent;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
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

			if (IPAddress.TryParse(address, out var parsedIp))
				_endPoint = new IPEndPoint(parsedIp, port);

			_connectTcs = new TaskCompletionSource<bool>();

			_connection.Connected                  += HandleConnected;
			_connection.ConnectionShutdown         += HandleConnectionShutdown;
			_connection.IncomingStream             += HandleIncomingStream;
			_connection.DatagramReceived           += HandleDatagramReceived;
			_connection.UnobservedException        += HandleUnobservedException;
			_connection.ConnectionShutdownComplete += HandleConnectionShutdownComplete;
			_connection.CertificateReceived        += HandleCertificateReceived;

			try {
				Logger.LogDebug($"Starting connection to {address}:{port} with ALPN '{_connection.NegotiatedAlpn}'...", tag: nameof(QuicConnector));
				// Start is fire-and-forget; Connected / ConnectionShutdown drive the TCS
				_connection.Start(address, port);


				var timeout  = Task.Delay(ConnectTimeoutMs);
				var finished = await Task.WhenAny(_connectTcs.Task, timeout).ConfigureAwait(false);

				if (finished == timeout)
					throw new TimeoutException($"Connection attempt to {address}:{port} timed out after {ConnectTimeoutMs}ms.");

				return await _connectTcs.Task.ConfigureAwait(false);
			} catch (Exception ex) {
				Logger.LogError(new Exception($"Exception during Connect to {address}:{port}", ex), tag: nameof(QuicConnector));
				await Close();
				return false;
			} finally {
				await UniTask.SwitchToMainThread();
			}
		}
		private int HandleCertificateReceived(QuicPeerConnection peer, X509Certificate2 certificate, SignedCms chain, uint deferredErrorFlags, int deferredStatus) {
			Logger.LogDebug($"Certificate received from {peer.RemoteEndPoint}: Subject='{certificate.Subject}', Issuer='{certificate.Issuer}', DeferredErrorFlags={deferredErrorFlags}, DeferredStatus={deferredStatus}", tag: nameof(QuicConnector));
			return 0; // Accept the certificate (no validation)
		}
		
		private void HandleConnectionShutdownComplete(QuicPeerConnection sender, bool arg1, bool arg2, bool arg3) {
			Logger.LogDebug($"ConnectionShutdownComplete for {sender.RemoteEndPoint} (byPeer={arg1}, byTransport={arg2}, appCloseInProgress={arg3})", tag: nameof(QuicConnector));
		}

		private void HandleUnobservedException(QuicPeerConnection sender, ExceptionDispatchInfo arg) {
			Logger.LogError(new Exception($"Unobserved exception in connection to {sender.RemoteEndPoint}", arg.SourceException), tag: nameof(QuicConnector));
			// No recovery possible — trigger shutdown logic to clean up and fire events.
			try { sender.Shutdown(); } catch {
				// ignored
			}
		}

		// ── QuicClientConnection callbacks ──────────────────────────────────

		private void HandleConnected(QuicClientConnection conn) {
			Logger.LogDebug($"Connected to {conn.RemoteEndPoint} with ALPN '{conn.NegotiatedAlpn}'", tag: nameof(QuicConnector));
			_isConnected = true;
			_endPoint    = conn.RemoteEndPoint;
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
			Logger.LogWarning($"ConnectionShutdown: {reason} (byPeer={initiatedByPeer}, byTransport={initiatedByTransport}, code={errorCode})", tag: nameof(QuicConnector));
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

				UniTask.Post(() => {
					var buff = new Buffer();
					buff.Write(buf, 0, read);
					buff.Start();
					OnReceived?.Invoke(buff);
				});
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
				try { s.Dispose(); } catch {
					// ignored
				}

			try { conn.Dispose(); } catch (Exception) {
				// ignored
			}
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