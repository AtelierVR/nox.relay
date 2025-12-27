using System;
using System.Net;
using System.Net.Sockets;
using Cysharp.Threading.Tasks;
using Nox.CCK.Utils;
using UnityEngine.Events;
using Buffer = Nox.CCK.Utils.Buffer;

namespace Nox.Relay.Core.Connectors {
	/// <summary>
	/// TCP connector implementation for handling TCP network connections.
	/// </summary>
	public class TcpConnector : IConnector {
		public const string ProtocolName = "tcp";

		private Socket               _socket;
		private ushort               _bufferSize = 1024;
		private SocketAsyncEventArgs _recArgs;

		// Cumulative buffer for TCP (data arriving in pieces)
		private Buffer _accumulator = new();
		private bool   _receiving;

		public EndPoint EndPoint
			=> _socket?.RemoteEndPoint;

		public UnityEvent<Buffer> OnReceived { get; } = new();

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
				ip = hostEntry.AddressList[0];
			}

			_socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

			_recArgs           =  new SocketAsyncEventArgs();
			_recArgs.Completed += OnReceiveCompleted;
			Mtu                =  _bufferSize;

			await _socket.ConnectAsync(ip, port);

			if (_socket.Connected)
				StartReceiveLoop();

			return _socket.Connected;
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

			// true => async; false => completed immediately
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
				return;
			}

			// Add the new bytes to the cumulative buffer
			_accumulator.Write(e.Buffer.AsSpan(0, e.BytesTransferred).ToArray());

			// Position at the beginning for reading
			_accumulator.Start();


			// As long as we can decode a complete message
			while (_accumulator.Remaining >= sizeof(ushort)) {
				var len = _accumulator.ReadUShort();
				_accumulator.Move(-sizeof(ushort));

				if (_accumulator.Remaining < len) 
					break;

				var payload = _accumulator.ReadBytes(len);
				var packet  = new Buffer();
				packet.Write(payload);

				OnReceived.Invoke(packet);
			}

			// Compact the buffer (remove what has been consumed)
			_accumulator.Compact();

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

			_accumulator = new Buffer();

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

			var data = buffer.ToBuffer();
			var args = new SocketAsyncEventArgs();
			args.SetBuffer(data, 0, data.Length);

			var tcs = new UniTaskCompletionSource<bool>();

			args.Completed += Handler;

			// true => async; false => completed immediately
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