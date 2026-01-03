using System;
using System.Net;
using System.Net.Sockets;
using Cysharp.Threading.Tasks;
using UnityEngine.Events;
using Buffer = Nox.CCK.Utils.Buffer;

namespace Nox.Relay.Core.Connectors {
	/// <summary>
	/// UDP connector implementation for handling UDP network connections.
	/// </summary>
	public class UdpConnector : IConnector {
		public const string ProtocolName = "udp";

		private Socket               _socket;
		private ushort               _bufferSize = 1024;
		private SocketAsyncEventArgs _recArgs;
		private bool                 _receiving;

		public EndPoint EndPoint
			=> _socket?.RemoteEndPoint;

		public UnityEvent<Buffer> OnReceived { get; } = new();

		public UnityEvent         OnConnected    { get; } = new();
		
		public UnityEvent<string> OnDisconnected { get; } = new();

		public ushort Mtu {
			get => (ushort)(_socket?.ReceiveBufferSize ?? _bufferSize);
			set {
				_bufferSize = value;
				_recArgs?.SetBuffer(new byte[value], 0, value);
				if (_socket == null) return;
				_socket.ReceiveBufferSize = value;
				_socket.SendBufferSize    = value;
			}
		}

		public string Protocol
			=> ProtocolName;

		public bool IsConnected
			=> _socket is { Connected: true };

		/// <summary>
		/// Connects to the specified address and port asynchronously.
		/// </summary>
		/// <param name="address">The IP address or hostname to connect to.</param>
		/// <param name="port">The port number to connect to.</param>
		/// <returns>True if the connection was successful, false otherwise.</returns>
		public async UniTask<bool> Connect(string address, ushort port) {
			await Close();

			if (!IPAddress.TryParse(address, out var ip)) {
				var hostEntry = await Dns.GetHostEntryAsync(address);
				if (hostEntry.AddressList.Length == 0) 
					return false;
				ip = hostEntry.AddressList[0];
			}

			_socket = new Socket(ip.AddressFamily, SocketType.Dgram, ProtocolType.Udp);

			_recArgs           =  new SocketAsyncEventArgs();
			_recArgs.Completed += OnReceiveCompleted;

			Mtu = _bufferSize;

			await _socket.ConnectAsync(ip, port);

			if (_socket.Connected) {
				StartReceiveLoop();
				OnConnected.Invoke();
				return true;
			}

			return false;
		}

		/// <summary>
		/// Starts the receive loop if not already receiving.
		/// </summary>
		private void StartReceiveLoop() {
			if (_receiving || _recArgs == null || _socket == null)
				return;

			_receiving = true;
			TryReceive();
		}

		/// <summary>
		/// Attempts to receive data asynchronously.
		/// </summary>
		private void TryReceive() {
			if (_socket == null || _recArgs == null)
				return;

			_recArgs.SetBuffer(_recArgs.Buffer, 0, _bufferSize);

			// true => async; false => completed synchronously
			if (!_socket.ReceiveAsync(_recArgs))
				OnReceiveCompleted(this, _recArgs);
		}

		/// <summary>
		/// Handles the completion of a receive operation.
		/// </summary>
		/// <param name="sender">The sender of the event.</param>
		/// <param name="e">The socket async event args.</param>
		private void OnReceiveCompleted(object sender, SocketAsyncEventArgs e) {
			if (e.SocketError != SocketError.Success || e.BytesTransferred == 0) {
				_receiving = false;
				OnDisconnected.Invoke("Disconnected");
				return;
			}

		var buffer = new Buffer();
		buffer.Write(e.Buffer.AsSpan(0, e.BytesTransferred).ToArray());
		buffer.Start();

		while (buffer.Remaining >= sizeof(ushort)) {
			var len = buffer.ReadUShort();
			
			// Move back to include the length field in the packet
			buffer.Move(-sizeof(ushort));
			
			// Check if we have enough data (len includes the 2-byte length field itself)
			if (buffer.Remaining < len)
				break;

			// Skip empty or invalid packets (length must be at least 2 for the length field itself)
			if (len < 2) {
				buffer.ReadBytes(2); // Consume at least the length field to avoid infinite loop
				continue;
			}

			var data   = buffer.ReadBytes(len); // Read complete packet including length field
			var packet = new Buffer();
			packet.Write(data);
			OnReceived.Invoke(packet);
		}

			if (_receiving)
				TryReceive();
		}

		/// <summary>
		/// Closes the connection and cleans up resources.
		/// </summary>
		/// <returns>A completed task.</returns>
		public UniTask Close() {
			_receiving = false;

			if (_recArgs != null) {
				_recArgs.Completed -= OnReceiveCompleted;
				_recArgs.Dispose();
				_recArgs = null;
			}

			_socket?.Close();
			_socket = null;

			OnDisconnected.Invoke("Closed");

			return UniTask.CompletedTask;
		}

		/// <summary>
		/// Sends data asynchronously over the connection.
		/// </summary>
		/// <param name="buffer">The buffer containing the data to send.</param>
		/// <returns>True if the send was successful, false otherwise.</returns>
		public UniTask<bool> Send(Buffer buffer) {
			if (!IsConnected)
				return UniTask.FromResult(false);

			var data = buffer.ToArray();
			var args = new SocketAsyncEventArgs();
			args.SetBuffer(data, 0, data.Length);

			var tcs = new UniTaskCompletionSource<bool>();

			args.Completed += Handler;

			// true => async; false => already completed
			if (!_socket.SendAsync(args))
				Complete(args);

			return tcs.Task;

			void Handler(object s, SocketAsyncEventArgs e)
				=> Complete(e);

			void Complete(SocketAsyncEventArgs e) {
				tcs.TrySetResult(e.SocketError == SocketError.Success);
				args.Completed -= Handler;
				args.Dispose();
			}
		}
	}
}