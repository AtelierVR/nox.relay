using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using Buffer = Nox.CCK.Utils.Buffer;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Relay.Core.Connectors {
	public class TcpConnector : IConnector {
		public const string PROTOCOL_NAME = "tcp";
		private const int DEFAULT_BUFFER_SIZE = 65536; // Augmenté de 8192 à 65536
		private const int MAX_MESSAGE_SIZE = 65535; // ushort.MaxValue
		private const int CONNECT_TIMEOUT_MS = 5000;

		private Socket _socket;
		private CancellationTokenSource _receiveCts;
		private readonly Buffer _receiveBuffer = new();
		private readonly byte[] _tempBuffer = new byte[DEFAULT_BUFFER_SIZE];
		private readonly object _sendLock = new object();
		private bool _isDisposing;

		public string Protocol => PROTOCOL_NAME;
		
		public bool IsConnected => _socket is { Connected: true } && !_isDisposing;
		
		public EndPoint EndPoint => _socket?.LocalEndPoint;
		
		public ushort Mtu { get; set; } = 1460; 
		
		public UnityEvent<Buffer> OnReceived { get; } = new();
		
		public UnityEvent OnConnected { get; } = new();
		
		public UnityEvent<string> OnDisconnected { get; } = new ();

		public async UniTask<bool> Connect(string address, ushort port) {
			if (IsConnected) {
				Logger.LogWarning($"Already connected to {address}", tag: nameof(TcpConnector));
				return false;
			}

			try {
				if (!IPAddress.TryParse(address, out var ipAddress)) {
					var hostEntry = await Dns.GetHostEntryAsync(address);
					ipAddress = hostEntry.AddressList.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork);
					
					if (ipAddress == null) {
						Logger.LogError($"No IPv4 address found for {address}", tag: nameof(TcpConnector));
						return false;
					}
				}

				_socket = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
				_socket.NoDelay = true;
				_socket.ReceiveBufferSize = DEFAULT_BUFFER_SIZE;
				_socket.SendBufferSize = DEFAULT_BUFFER_SIZE;

				var connectTask = _socket.ConnectAsync(ipAddress, port).AsUniTask();
				var timeoutTask = UniTask.Delay(CONNECT_TIMEOUT_MS);
				var winnerIndex = await UniTask.WhenAny(connectTask, timeoutTask);

				if (winnerIndex == 1) {
					_socket?.Close();
					_socket = null;
					Logger.LogError($"Trying to connect to {address}:{port}", tag: nameof(TcpConnector));
					return false;
				}

				if (!_socket.Connected) {
					Logger.LogError($"Failed to connect to {address}:{port}", tag: nameof(TcpConnector));
					_socket?.Close();
					_socket = null;
					return false;
				}

				_receiveCts = new CancellationTokenSource();
				// Exécuter la boucle de réception sur un thread pool pour éviter les changements de contexte
				UniTask.RunOnThreadPool(() => ReceiveLoopAsync(_receiveCts.Token), cancellationToken: _receiveCts.Token).Forget();

				await UniTask.SwitchToMainThread();
				OnConnected?.Invoke();

				return true;
			}
			catch (SocketException ex) {
				Logger.LogError(new Exception("Socket exception during connecting", ex), tag: nameof(TcpConnector));
				_socket?.Close();
				_socket = null;
				return false;
			}
			catch (Exception ex) {
				Logger.LogError(new Exception("Unexpected error during connecting", ex), tag: nameof(TcpConnector));
				_socket?.Close();
				_socket = null;
				return false;
			}
		}

		public async UniTask Close() {
			if (_socket == null) return;

			_isDisposing = true;

			try {
				// Annuler la boucle de réception
				_receiveCts?.Cancel();
				_receiveCts?.Dispose();
				_receiveCts = null;

				await UniTask.Delay(100);

				// Fermeture gracieuse
				if (_socket.Connected) 
					_socket.Shutdown(SocketShutdown.Both);

				_socket.Close();
			}
			catch (Exception ex) {
				Logger.LogError(new Exception("Error during TcpConnector.Close", ex), tag: nameof(TcpConnector));
			}
			finally {
				_socket = null;
				_isDisposing = false;
			}
		}

		public UniTask<bool> Send(Buffer buffer) {
			if (!IsConnected) {
				Logger.LogError("Cannot send: not connected", tag: nameof(TcpConnector));
				return UniTask.FromResult(false);
			}

			if (buffer == null || buffer.Length == 0) {
				Logger.LogError("Cannot send: empty buffer", tag: nameof(TcpConnector));
				return UniTask.FromResult(false);
			}

			try {
				lock (_sendLock) {
					var sent = 0;
					while (sent < buffer.Length) {
						var bytesToSend = Math.Min(buffer.Length - sent, Mtu);
						var sentNow = _socket.Send(buffer.Data, sent, bytesToSend, SocketFlags.None);
						
						if (sentNow <= 0) {
							Logger.LogError("Send failed: connection closed", tag: nameof(TcpConnector));
							HandleDisconnection("Send failed");
							return UniTask.FromResult(false);
						}
						
						sent += sentNow;
					}
				}

				return UniTask.FromResult(true);
			}
			catch (SocketException ex) {
				Logger.LogError(new Exception("Socket exception during send", ex), tag: nameof(TcpConnector));
				HandleDisconnection($"Send error: {ex.Message}");
				return UniTask.FromResult(false);
			}
			catch (Exception ex) {
				Logger.LogError(new Exception("Unexpected error during send", ex), tag: nameof(TcpConnector));
				return UniTask.FromResult(false);
			}
		}

		private async UniTaskVoid ReceiveLoopAsync(CancellationToken cancellationToken) {
			try {
				while (!cancellationToken.IsCancellationRequested && IsConnected) {
					int received;
					try {
						// Utilisation de ConfigureAwait(false) pour éviter de capturer le SynchronizationContext sur le ThreadPool
						var receiveTask = _socket.ReceiveAsync(new ArraySegment<byte>(_tempBuffer), SocketFlags.None);
						received = await receiveTask.ConfigureAwait(false);
					}
					catch (OperationCanceledException) {
						break;
					}

					if (received <= 0) {
						HandleDisconnection("Connection closed by remote host");
						break;
					}

					if (!AppendToReceiveBuffer(received)) {
						Logger.LogError("Receive buffer overflow", tag: nameof(TcpConnector));
						HandleDisconnection("Buffer overflow");
						break;
					}

					ProcessReceivedData();
				}
			}
			catch (SocketException ex) {
				if (!cancellationToken.IsCancellationRequested && !_isDisposing) {
					Logger.LogError(new Exception("Socket exception in receive loop", ex), tag: nameof(TcpConnector));
					HandleDisconnection($"Receive error: {ex.Message}");
				}
			}
			catch (Exception ex) {
				if (!cancellationToken.IsCancellationRequested && !_isDisposing) {
					Logger.LogError(new Exception("Unexpected error in receive loop", ex), tag: nameof(TcpConnector));
					HandleDisconnection($"Unexpected error: {ex.Message}");
				}
			}
		}

		private bool AppendToReceiveBuffer(int bytesReceived) {
			var newLength = _receiveBuffer.Length + bytesReceived;
			if (newLength > _receiveBuffer.Data.Length) {
				var newSize = Math.Max(_receiveBuffer.Data.Length * 2, newLength);
				
				if (newSize > MAX_MESSAGE_SIZE * 2) {
					Logger.LogError($"Receive buffer too large: {newSize} bytes", tag: nameof(TcpConnector));
					return false;
				}

				var newData = new byte[newSize];
				Array.Copy(_receiveBuffer.Data, newData, _receiveBuffer.Length);
				_receiveBuffer.Data = newData;
			}

			Array.Copy(_tempBuffer, 0, _receiveBuffer.Data, _receiveBuffer.Length, bytesReceived);
			_receiveBuffer.Length = (ushort)newLength;

			return true;
		}

		private void ProcessReceivedData() {
			_receiveBuffer.Start(); 

			while (_receiveBuffer.Remaining >= 2) {
				var messageLength = _receiveBuffer.ReadUShort();

				if (messageLength < 2) {
					Logger.LogError($"Invalid message length: {messageLength} (must be >= 2)", tag: nameof(TcpConnector));
					HandleDisconnection("Invalid message length");
					return;
				}
				
				var messageDataLength = messageLength - 2;
				if (_receiveBuffer.Remaining < messageDataLength) {
					_receiveBuffer.Move(-2);
					break;
				}

				var messageBuffer = new Buffer();
				messageBuffer.Write(messageLength); 
				var messageData = _receiveBuffer.ReadBytes((ushort)messageDataLength);
				messageBuffer.Write(messageData);
				messageBuffer.Start(); 
				
				// Invocation sur le main thread depuis le ThreadPool
				UniTask.Post(() => {
					try {
						OnReceived?.Invoke(messageBuffer);
					}
					catch (Exception ex) {
						Logger.LogError(new Exception("Error in OnReceived handler", ex), tag: nameof(TcpConnector));
					}
				});
			}

			_receiveBuffer.Compact();
		}

		private void HandleDisconnection(string reason) {
			if (_isDisposing) return;

			_isDisposing = true;

			try {
				_receiveCts?.Cancel();
				_socket?.Close();
				_socket = null;

				UniTask.Post(() => {
					try {
						OnDisconnected?.Invoke(reason);
					}
					catch (Exception ex) {
						Logger.LogError(new Exception("Error in OnDisconnected handler", ex), tag: nameof(TcpConnector));
					}
				});
			}
			catch (Exception ex) {
				Logger.LogError(new Exception("Error in OnDisconnected handler", ex), tag: nameof(TcpConnector));
			}
			finally {
				_isDisposing = false;
			}
		}
	}
}