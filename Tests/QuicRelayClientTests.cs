using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Nox.Relay.Core.Connectors;
using NUnit.Framework;
using Buffer = Nox.CCK.Utils.Buffer;

namespace Nox.Relay.Tests {
	/// <summary>
	/// Integration tests for <see cref="QuicConnector"/>.
	/// Each test spins up a local <see cref="TestQuicServer"/> and exercises one behaviour.
	/// </summary>
	[TestFixture]
	public class QuicRelayClientTests {
		// The server is heavy (starts a real QUIC listener) — create it once for the
		// whole fixture to avoid rapid registration/teardown cycles in MsQuic.
		private static TestQuicServer? _sharedServer;

		private QuicConnector? _client;

		// ── Fixture lifecycle (server) ────────────────────────────────────────

		[OneTimeSetUp]
		public static void FixtureSetUp() {
			_sharedServer = new TestQuicServer();
		}

		[OneTimeTearDown]
		public static void FixtureTearDown() {
			_sharedServer?.Dispose();
			_sharedServer = null;
		}

		// ── Per-test lifecycle (client) ───────────────────────────────────────

		[SetUp]
		public void SetUp() {
			_client = new QuicConnector();
		}

		[TearDown]
		public async Task TearDown() {
			if (_client != null) {
				await _client.Close().AsTask().ConfigureAwait(false);
				_client.Dispose();
				_client = null;
			}

			// Allow MsQuic's internal connection shutdown to complete before
			// the next test creates a new connection to the same server.
			await Task.Delay(300).ConfigureAwait(false);
		}

		// ── Connection tests ──────────────────────────────────────────────────

		[Test, Timeout(15_000)]
		public async Task Connect_AlpnShouldBeNegotiated_AfterSuccessfulConnect() {
			// Regression test: ensures the server advertises ALPN "relay" and
			// MsQuic accepts the handshake. If the Rust server's TLS config omits
			// alpn_protocols, the connection is refused before reaching this point.
			var result = await ConnectToServer().ConfigureAwait(false);
			Assert.That(result, Is.True,
				"Handshake failed — server may not advertise ALPN 'relay'. " +
				"Check tls.rs: tls_config.alpn_protocols = vec![b\"relay\".to_vec()]");
		}

		[Test, Timeout(15_000)]
		public async Task Connect_ShouldReturnTrue_WhenServerIsAvailable() {
			var result = await ConnectToServer().ConfigureAwait(false);
			Assert.That(result, Is.True);
		}

		[Test, Timeout(20_000)]
		public async Task Connect_ShouldReturnFalse_WhenServerIsDown() {
			// Use a fresh server to find a free port, immediately stop it, then try to connect.
			ushort closedPort;
			using (var tempServer = new TestQuicServer()) {
				closedPort = (ushort)tempServer.LocalEndPoint.Port;
			} // server disposed — nothing listening on closedPort anymore

			var result = await _client!
				.Connect("127.0.0.1", closedPort)
				.AsTask().ConfigureAwait(false);

			Assert.That(result, Is.False);
		}

		[Test, Timeout(15_000)]
		public async Task IsConnected_ShouldBeTrue_AfterSuccessfulConnect() {
			await ConnectToServer().ConfigureAwait(false);
			Assert.That(_client!.IsConnected, Is.True);
		}

		[Test, Timeout(15_000)]
		public async Task IsConnected_ShouldBeFalse_BeforeConnect() {
			Assert.That(_client!.IsConnected, Is.False);
		}

		[Test, Timeout(15_000)]
		public async Task EndPoint_ShouldMatchServerAddress_AfterConnect() {
			await ConnectToServer().ConfigureAwait(false);
			Assert.That(_client!.EndPoint, Is.Not.Null);
		}

		// ── OnConnected event ─────────────────────────────────────────────────

		[Test, Timeout(15_000)]
		public async Task OnConnected_ShouldFireWithTrue_AfterSuccessfulConnect() {
			var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			_client!.OnConnected.AddListener((res) => tcs.TrySetResult(res));

			await ConnectToServer().ConfigureAwait(false);

			var fired = await tcs.Task.ConfigureAwait(false);
			Assert.That(fired, Is.True);
		}

		// ── OnDisconnect event ────────────────────────────────────────────────

		[Test, Timeout(15_000)]
		public async Task OnDisconnect_ShouldFire_WhenServerDisconnectsClient() {
			await ConnectToServer().ConfigureAwait(false);

			// Wait for the server's ClientConnected event to fire and set _activeConnection,
			// which happens slightly after the client's Connected event.
			await Task.Delay(200).ConfigureAwait(false);

			var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
			_client!.OnDisconnected.AddListener(((reason) => tcs.TrySetResult(reason)));

			_sharedServer!.DisconnectClient();

			var reason = await tcs.Task.ConfigureAwait(false);
			Assert.That(reason, Is.Not.Null);
		}

		[Test, Timeout(15_000)]
		public async Task Close_ShouldSetIsConnectedToFalse() {
			await ConnectToServer().ConfigureAwait(false);
			Assert.That(_client!.IsConnected, Is.True);

			await _client.Close().AsTask().ConfigureAwait(false);

			Assert.That(_client.IsConnected, Is.False);
		}

		// ── Send Stream ───────────────────────────────────────────────────────

		[Test, Timeout(15_000)]
		public async Task Send_Stream_ShouldReturnTrue_WhenConnected() {
			await ConnectToServer().ConfigureAwait(false);
			var buff = new Buffer();
			buff.Write("hello stream");
			var result = await _client!
				.Send(buff, SendType.Stream)
				.AsTask().ConfigureAwait(false);

			Assert.That(result, Is.True);
		}

		[Test, Timeout(15_000)]
		public async Task Send_Stream_ShouldBeReceivedByServer() {
			await ConnectToServer().ConfigureAwait(false);

			var waitData = _sharedServer!.WaitForDataAsync();
			var buff     = new Buffer();
			buff.Write("stream payload");
			await _client!.Send(buff, SendType.Stream).AsTask().ConfigureAwait(false);

			var received = await waitData.ConfigureAwait(false);
			Assert.That(Text(received), Is.EqualTo("stream payload"));
		}

		[Test, Timeout(15_000)]
		public async Task Send_Stream_ShouldReturnFalse_WhenNotConnected() {
			var buff = new Buffer();
			buff.Write("data");
			var result = await _client!
				.Send(buff, SendType.Stream)
				.AsTask().ConfigureAwait(false);

			Assert.That(result, Is.False);
		}

		// ── Send Datagram ─────────────────────────────────────────────────────

		[Test, Timeout(15_000)]
		public async Task Send_Datagram_ShouldReturnTrue_WhenConnected() {
			await ConnectToServer().ConfigureAwait(false);

			var buff = new Buffer();
			buff.Write("hello datagram");
			var result = await _client!
				.Send(buff, SendType.Datagram)
				.AsTask().ConfigureAwait(false);

			Assert.That(result, Is.True);
		}

		[Test, Timeout(15_000)]
		public async Task Send_Datagram_ShouldBeReceivedByServer() {
			await ConnectToServer().ConfigureAwait(false);

			var waitData = _sharedServer!.WaitForDataAsync();
			var buff     = new Buffer();
			buff.Write("dgram payload");
			await _client!.Send(buff, SendType.Datagram)
				.AsTask().ConfigureAwait(false);

			var received = await waitData.ConfigureAwait(false);
			Assert.That(Text(received), Is.EqualTo("dgram payload"));
		}

		[Test, Timeout(15_000)]
		public async Task Send_Datagram_ShouldReturnFalse_WhenNotConnected() {
			var buff = new Buffer();
			buff.Write("data");
			var result = await _client!
				.Send(buff, SendType.Datagram)
				.AsTask().ConfigureAwait(false);

			Assert.That(result, Is.False);
		}

		// ── OnReceived (server → client) ──────────────────────────────────────

		[Test, Timeout(15_000)]
		public async Task OnReceived_ShouldFire_WhenServerSendsStream() {
			await ConnectToServer().ConfigureAwait(false);

			var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
			_client!.OnReceived.AddListener((data) => tcs.TrySetResult(data));

			// Give the client a moment to register before the server sends
			await Task.Delay(100).ConfigureAwait(false);
			await _sharedServer!.SendStreamAsync(Encoding.UTF8.GetBytes("server stream")).ConfigureAwait(false);

			var received = await tcs.Task.ConfigureAwait(false);
			Assert.That(Text(received), Is.EqualTo("server stream"));
		}

		[Test, Timeout(15_000)]
		public async Task OnReceived_ShouldFire_WhenServerSendsDatagram() {
			await ConnectToServer().ConfigureAwait(false);

			var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
			_client!.OnReceived.AddListener((data) => tcs.TrySetResult(data));

			await Task.Delay(100).ConfigureAwait(false);
			_sharedServer!.SendDatagram(Encoding.UTF8.GetBytes("server datagram"));

			var received = await tcs.Task.ConfigureAwait(false);
			Assert.That(Text(received), Is.EqualTo("server datagram"));
		}

		// ── Multiple messages ─────────────────────────────────────────────────

		[Test, Timeout(30_000)]
		public async Task Send_MultipleStreams_ShouldAllBeReceivedByServer() {
			await ConnectToServer().ConfigureAwait(false);

			for (var i = 0; i < 5; i++) {
				var payload  = $"msg-{i}";
				var waitData = _sharedServer!.WaitForDataAsync();
				var buff     = new Buffer();
				buff.Write(payload);
				await _client!.Send(buff, SendType.Stream).AsTask().ConfigureAwait(false);
				var received = await waitData.ConfigureAwait(false);
				Assert.That(Text(received), Is.EqualTo(payload), $"Message {i} did not match");
			}
		}

		// ── Helpers ───────────────────────────────────────────────────────────

		private Task<bool> ConnectToServer() {
			var ep = _sharedServer!.LocalEndPoint;
			return _client!.Connect(ep.Address.ToString(), (ushort)ep.Port).AsTask();
		}

		private static ReadOnlyMemory<byte> Bytes(string s)
			=> new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(s));

		// Buffer.Write(string) wire format: [ushort length][bytes]
		private static string Text(byte[] b) {
			if (b.Length >= 2) {
				var len = (b[0] << 8) | b[1];
				if (len + 2 == b.Length)
					return Encoding.UTF8.GetString(b, 2, len);
			}
			return Encoding.UTF8.GetString(b);
		}
	}
}