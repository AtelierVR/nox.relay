using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using StirlingLabs.MsQuic;
using StirlingLabs.MsQuic.Bindings;

namespace Nox.Relay.Tests
{
    /// <summary>
    /// A minimal in-process QUIC relay server used by unit tests.
    /// Creates a self-signed certificate at startup and listens on a random local port.
    /// </summary>
    internal sealed class TestQuicServer : IDisposable
    {
        private readonly QuicRegistration _registration;
        private readonly QuicServerConfiguration _serverConfig;
        private readonly QuicListener _listener;
        private readonly IPEndPoint _localEndPoint;

        private QuicServerConnection? _activeConnection;
        private TaskCompletionSource<byte[]>? _receiveWaiter;
        private bool _disposed;

        // ── Exposed to tests ────────────────────────────────────────────────

        public IPEndPoint LocalEndPoint => _localEndPoint;

        // ── Construction ─────────────────────────────────────────────────────

        private const string P12Password = "nox-test";

        public TestQuicServer()
        {
            // 1. Generate a self-signed RSA certificate in PKCS#12 format.
            var p12Stream = CreateSelfSignedP12();

            var cert = new QuicCertificate(p12Stream, P12Password);

            // 2. Registration + server configuration (ALPN = "relay")
            _registration = new QuicRegistration("relay-server-test");
            _serverConfig = new QuicServerConfiguration(_registration, "relay");
            _serverConfig.ConfigureCredentials(cert);

            // 3. Listener
            _listener = new QuicListener(_serverConfig);

            _listener.NewConnection += (_, conn) =>
            {
                // Accept any client certificate
                conn.CertificateReceived += (_, _, _, _, _) => 0;
            };

            _listener.ClientConnected += (_, conn) =>
            {
                Interlocked.Exchange(ref _activeConnection, conn);
                AttachHandlers(conn);
            };

            // 4. Find a free local port and start listening — retry on ADDRESS_IN_USE
            //    (Windows may hold UDP ports for a few seconds after a process exits)
            const int maxAttempts = 10;
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                ushort port = FindFreePort();
                _localEndPoint = new IPEndPoint(IPAddress.Loopback, port);
                try
                {
                    _listener.Start(_localEndPoint);
                    break; // success
                }
                catch (MsQuicException ex) when (ex.Status == MsQuic.QUIC_STATUS_ADDRESS_IN_USE
                                                 && attempt < maxAttempts - 1)
                {
                    // Port still held by OS — pick another one
                    System.Threading.Thread.Sleep(50);
                }
            }
        }

        // ── Helpers for tests ────────────────────────────────────────────────

        /// <summary>
        /// Returns the next received payload (from either a stream or a datagram).
        /// Awaiting this method before triggering a send ensures the test does not miss data.
        /// </summary>
        public Task<byte[]> WaitForDataAsync(int timeoutMs = 5_000)
        {
            var tcs = Interlocked.Exchange(ref _receiveWaiter,
                new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously));

            _ = Task.Delay(timeoutMs).ContinueWith(_ =>
                (_receiveWaiter ?? tcs).TrySetException(
                    new TimeoutException($"No data received within {timeoutMs} ms")));

            return (_receiveWaiter ?? tcs).Task;
        }

        /// <summary>Sends a stream message to the last accepted client.</summary>
        public async Task SendStreamAsync(byte[] data)
        {
            var conn = _activeConnection ?? throw new InvalidOperationException("No active connection");
            using var stream = conn.OpenStream();
            await stream.SendAsync(data, QUIC_SEND_FLAGS.FIN).ConfigureAwait(false);
        }

        /// <summary>Sends a datagram to the last accepted client.</summary>
        public void SendDatagram(byte[] data)
        {
            var conn = _activeConnection ?? throw new InvalidOperationException("No active connection");
            conn.SendDatagram(data);
        }

        /// <summary>Closes the connection to the current client (simulates server-side disconnect).</summary>
        public void DisconnectClient()
        {
            var conn = Interlocked.Exchange(ref _activeConnection, null);
            try { conn?.Shutdown(); } catch { }
            try { conn?.Dispose(); } catch { }
        }

        // ── Private ──────────────────────────────────────────────────────────

        private void AttachHandlers(QuicServerConnection conn)
        {
            conn.IncomingStream += (_, stream) =>
            {
                stream.DataReceived += s =>
                {
                    var available = (int)s.DataAvailable;
                    if (available <= 0) return;

                    var buf = new byte[available];
                    var read = s.Receive(new Span<byte>(buf));
                    if (read <= 0) return;

                    var data = new byte[read];
                    Array.Copy(buf, data, read);
                    CompleteWaiter(data);
                };
            };

            conn.DatagramReceived += (_, span) =>
            {
                var data = new byte[span.Length];
                span.CopyTo(data);
                CompleteWaiter(data);
            };
        }

        private void CompleteWaiter(byte[] data)
        {
            var waiter = Interlocked.Exchange(ref _receiveWaiter, null);
            waiter?.TrySetResult(data);
        }

        // ── Certificate generation ────────────────────────────────────────────

        private static MemoryStream CreateSelfSignedP12()
        {
            // Use BouncyCastle for fully-managed PKCS#12 generation (works on Mono/Unity)
            var keyGenParams = new RsaKeyGenerationParameters(
                BigInteger.ValueOf(65537),
                new SecureRandom(),
                2048,
                80);
            var keyGen = new RsaKeyPairGenerator();
            keyGen.Init(keyGenParams);
            var keyPair = keyGen.GenerateKeyPair();

            var certGen = new X509V3CertificateGenerator();
            certGen.SetSerialNumber(BigInteger.ProbablePrime(120, new Random()));
            var dn = new X509Name("CN=localhost");
            certGen.SetSubjectDN(dn);
            certGen.SetIssuerDN(dn);
            certGen.SetNotBefore(DateTime.UtcNow.AddDays(-1));
            certGen.SetNotAfter(DateTime.UtcNow.AddYears(2));
            certGen.SetPublicKey(keyPair.Public);

            certGen.AddExtension(X509Extensions.BasicConstraints, true,
                new BasicConstraints(false));
            certGen.AddExtension(X509Extensions.KeyUsage, true,
                new KeyUsage(KeyUsage.DigitalSignature));
            certGen.AddExtension(X509Extensions.ExtendedKeyUsage, false,
                new ExtendedKeyUsage(KeyPurposeID.IdKPServerAuth));

            var cert = certGen.Generate(
                new Asn1SignatureFactory("SHA256WithRSA", keyPair.Private));

            // Use 3DES-SHA1 — the classic PKCS#12 PBE algorithm supported by both
            // BouncyCastle on Mono and OpenSSL (msquic-openssl)
            var store = new Pkcs12StoreBuilder()
                .SetKeyAlgorithm(PkcsObjectIdentifiers.PbeWithShaAnd3KeyTripleDesCbc)
                .SetCertAlgorithm(PkcsObjectIdentifiers.PbeWithShaAnd3KeyTripleDesCbc)
                .Build();

            var certEntry = new X509CertificateEntry(cert);
            store.SetCertificateEntry("localhost", certEntry);
            store.SetKeyEntry("localhost",
                new AsymmetricKeyEntry(keyPair.Private),
                new[] { certEntry });

            var ms = new MemoryStream();
            store.Save(ms, P12Password.ToCharArray(), new SecureRandom());
            ms.Position = 0;
            return ms;
        }

        // ── Port discovery ────────────────────────────────────────────────────

        private static ushort FindFreePort()
        {
            var tmp = new TcpListener(IPAddress.Loopback, 0);
            tmp.Start();
            var port = (ushort)((IPEndPoint)tmp.LocalEndpoint).Port;
            tmp.Stop();
            return port;
        }

        // ── IDisposable ───────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _listener.Stop(); } catch { }
            try { _listener.Dispose(); } catch { }

            DisconnectClient();

            try { _serverConfig.Dispose(); } catch { }
            try { _registration.Dispose(); } catch { }
        }
    }
}
