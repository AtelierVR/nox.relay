using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using StirlingLabs.MsQuic;
using StirlingLabs.MsQuic.Bindings;
using UnityEngine.Events;
using Logger = Nox.CCK.Utils.Logger;
using Buffer = Nox.CCK.Utils.Buffer;

// StirlingLabs.MsQuic targets netstandard2.0 and wraps the native msquic library.
// Unity setup: add StirlingLabs.MsQuic.dll + platform msquic native library as
// Plugins, then list the managed DLLs in Nox.Relay.Core.asmdef → precompiledReferences.

namespace Nox.Relay.Core.Connectors
{
	/// <summary>
	/// QUIC connector for the Nox relay protocol, backed by <b>StirlingLabs.MsQuic</b>
	/// (a managed netstandard2.0 wrapper over the <c>msquic</c> native library).
	/// <para>
	/// Implements three QUIC channels required by the relay server:
	/// <list type="bullet">
	///   <item><term>Bi-directional streams</term><description>One per request/response pair.</description></item>
	///   <item><term>Inbound uni-directional streams</term><description>Server-initiated push (Join, Leave, …).</description></item>
	///   <item><term>QUIC datagrams</term><description>Unreliable Transform (0x0B) and Voice (0x14).</description></item>
	/// </list>
	/// </para>
	/// </summary>
	public class QuicConnector : IConnector
	{
		// ─── StirlingLabs.MsQuic implementation (netstandard2.0) ─────────────────
		/// <summary>Protocol name used in <see cref="ConnectorHelper.From"/>.</summary>
		public const string ProtocolName = "quic";

		/// <summary>ALPN protocol identifier sent during the TLS handshake.</summary>
		public static string AlpnProtocol = "noxrelay";

		// One registration is shared across all connector instances (msquic global state).
		private static QuicRegistration _sharedReg;
		private static readonly object _regLock = new object();

		private QuicClientConnection _connection;
		private CancellationTokenSource _cts;
		private ushort _mtu = 1200;

		/// <inheritdoc/>
		public string Protocol => ProtocolName;

		/// <inheritdoc/>
		public bool IsConnected => _connection != null && !(_cts?.IsCancellationRequested ?? true);

		/// <inheritdoc/>
		public EndPoint EndPoint => _connection?.LocalEndPoint;

		/// <inheritdoc/>
		public ushort Mtu { get => _mtu; set => _mtu = value; }

		/// <inheritdoc/>
		public UnityEvent<Buffer> OnReceived { get; } = new UnityEvent<Buffer>();

		/// <inheritdoc/>
		public UnityEvent OnConnected { get; } = new UnityEvent();

		/// <inheritdoc/>
		public UnityEvent<string> OnDisconnected { get; } = new UnityEvent<string>();

		// ─────────────────────────────────────────────────────────────────────────
		// IConnector methods
		// ─────────────────────────────────────────────────────────────────────────

		/// <inheritdoc/>
		public async UniTask<bool> Connect(string address, ushort port)
		{
			await Close();
			_cts = new CancellationTokenSource();

			try
			{
				var reg = GetSharedRegistration();
				using (var config = new QuicClientConfiguration(reg, AlpnProtocol))
				{
					// NO_CERTIFICATE_VALIDATION accepts the relay server's self-signed certificate.
					config.ConfigureCredentials(QUIC_CREDENTIAL_FLAGS.NO_CERTIFICATE_VALIDATION);

					_connection = new QuicClientConnection(config)
					{
						ReceiveDatagramsAsync = true
					};
				}

				_connection.IncomingStream += HandleIncomingStream;
				_connection.DatagramReceived += HandleDatagramReceived;

				await _connection.ConnectAsync(address, (ushort)port)
					.ContinueWith(t => t, _cts.Token);

				await UniTask.SwitchToMainThread();
				OnConnected.Invoke();
				return true;
			}
			catch (Exception ex)
			{
				Logger.LogError(new Exception("QuicConnector.Connect failed", ex), tag: nameof(QuicConnector));
				await Close();
				return false;
			}
		}

		/// <inheritdoc/>
		public UniTask Close()
		{
			if (_connection == null) return UniTask.CompletedTask;

			_cts?.Cancel();
			try { _connection.IncomingStream -= HandleIncomingStream; } catch { }
			try { _connection.DatagramReceived -= HandleDatagramReceived; } catch { }
			try { _connection.Close(); } catch { }
			try { _connection.Dispose(); } catch { }

			_connection = null;
			_cts?.Dispose();
			_cts = null;
			return UniTask.CompletedTask;
		}

		/// <inheritdoc/>
		/// <remarks>
		/// <see cref="SendType.Auto"/> routes via datagrams for Transform (0x0B) and Voice (0x14),
		/// and via a new bi-directional stream for all other packets.
		/// </remarks>
		public async UniTask<bool> Send(Buffer buffer, SendType type = SendType.Auto)
		{
			if (!IsConnected) return false;

			if (type == SendType.Auto)
				type = SendType.BiStream;

			try
			{
				if (type == SendType.Datagram)
				{
					// Strip the 2-byte stream length prefix — datagrams use [UID:u16][Type:u8][payload].
					var payload = new byte[buffer.Length - 2];
					Array.Copy(buffer.Data, 2, payload, 0, payload.Length);
					_connection.SendDatagram(new Memory<byte>(payload));
					return true;
				}

				// Open a bi-directional stream for this request/response pair.
				var stream = _connection.OpenStream();
				var frameTcs = new TaskCompletionSource<byte[]>();
				RegisterStreamFrameReader(stream, frameTcs, _cts.Token);

				var data = new byte[buffer.Length];
				Array.Copy(buffer.Data, data, buffer.Length);
				await stream.SendAsync(new ReadOnlyMemory<byte>(data));
				stream.Shutdown();

				var frame = await frameTcs.Task;
				if (frame != null)
					FireOnReceived(frame);
				return true;
			}
			catch (OperationCanceledException)
			{
				return false;
			}
			catch (Exception ex)
			{
				Logger.LogError(new Exception("QuicConnector.Send failed", ex), tag: nameof(QuicConnector));
				return false;
			}
		}

		// ─────────────────────────────────────────────────────────────────────────
		// Event handlers
		// ─────────────────────────────────────────────────────────────────────────

		/// <summary>Called when the server opens a server-initiated push stream.</summary>
		private void HandleIncomingStream(QuicPeerConnection _, QuicStream stream)
		{
			var ct = _cts?.Token ?? CancellationToken.None;
			var frameTcs = new TaskCompletionSource<byte[]>();
			RegisterStreamFrameReader(stream, frameTcs, ct);
			frameTcs.Task.ContinueWith(t =>
			{
				if (t.Status == TaskStatus.RanToCompletion && t.Result != null)
					FireOnReceived(t.Result);
			}, TaskContinuationOptions.ExecuteSynchronously);
		}

		/// <summary>Called for each received QUIC datagram (Transform / Voice).</summary>
		private void HandleDatagramReceived(QuicPeerConnection _, ReadOnlySpan<byte> dgram)
		{
			if (dgram.IsEmpty) return;
			// Prepend 2-byte length prefix: [Length:u16 BE][UID:u16][Type:u8][payload…]
			var len = (ushort)(dgram.Length + 2);
			var frame = new byte[len];
			frame[0] = (byte)(len >> 8);
			frame[1] = (byte)(len & 0xFF);
			dgram.CopyTo(frame.AsSpan(2));
			FireOnReceived(frame);
		}

		// ─────────────────────────────────────────────────────────────────────────
		// Frame reader helpers
		// ─────────────────────────────────────────────────────────────────────────

		private static void RegisterStreamFrameReader(
			QuicStream stream,
			TaskCompletionSource<byte[]> frameTcs,
			CancellationToken ct)
		{
			var reader = new StreamFrameBuffer(frameTcs);
			ct.Register(() => frameTcs.TrySetCanceled());
			stream.DataReceived += reader.OnData;
		}

		/// <summary>
		/// Buffers stream bytes and resolves a TCS when the first complete
		/// length-prefixed frame is available. Frame: [Length:u16 BE][payload…]
		/// </summary>
		private sealed class StreamFrameBuffer
		{
			private readonly TaskCompletionSource<byte[]> _tcs;
			private readonly List<byte> _buf = new List<byte>();
			private int _expectedLength = -1;

			internal StreamFrameBuffer(TaskCompletionSource<byte[]> tcs) => _tcs = tcs;

			public void OnData(QuicStream stream)
			{
				var tmp = new byte[4096];
				int n;
				while ((n = stream.Receive(tmp)) > 0)
					for (var i = 0; i < n; i++)
						_buf.Add(tmp[i]);
				TryResolve();
			}

			private void TryResolve()
			{
				if (_expectedLength < 0 && _buf.Count >= 2)
				{
					_expectedLength = (_buf[0] << 8) | _buf[1];
					if (_expectedLength < 2) { _tcs.TrySetResult(null); return; }
				}
				if (_expectedLength >= 2 && _buf.Count >= _expectedLength)
					_tcs.TrySetResult(_buf.GetRange(0, _expectedLength).ToArray());
			}
		}

		// ─────────────────────────────────────────────────────────────────────────

		private void FireOnReceived(byte[] frame)
		{
			var buf = new Buffer();
			buf.Write(frame);
			UniTask.Post(() => OnReceived.Invoke(buf));
		}

		private void HandleDisconnect(string reason)
		{
			Close().Forget();
			UniTask.Post(() => OnDisconnected.Invoke(reason));
		}

		private static QuicRegistration GetSharedRegistration()
		{
			if (_sharedReg != null) return _sharedReg;
			lock (_regLock)
			{
				if (_sharedReg == null) _sharedReg = new QuicRegistration("nox-relay");
				return _sharedReg;
			}
		}
	}
}
