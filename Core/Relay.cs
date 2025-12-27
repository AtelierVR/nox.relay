using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Nox.CCK.Utils;
using Nox.Relay.Core.Connectors;
using Nox.Relay.Core.Types;
using Nox.Relay.Core.Types.Contents;
using UnityEngine.Events;
using Buffer = Nox.CCK.Utils.Buffer;

namespace Nox.Relay.Core {
	/// <summary>
	/// Main Relay class for handling communication with the relay server.
	/// It manages sending and receiving packets,
	/// handling requests and responses,
	/// and maintaining connection state.
	/// A relay is hosted to a specific Node server via a connector implementing <see cref="IConnector"/>.
	/// </summary>
	public class Relay {
		/// <summary>
		/// Current protocol version used by the relay system.
		/// </summary>
		public const ushort ProtocolVersion = 1;

		/// <summary>
		/// Size of the relay packet header in bytes.
		/// Consists of:
		/// - Length <see cref="ushort"/> (2 bytes)
		/// - State  <see cref="ushort"/> (2 bytes)
		/// - Type   <see cref="RequestType"/> (1 byte)
		/// </summary>
		public const int HeaderSize = 5;

		/// <summary>
		/// Default timeout duration in seconds for relay requests.
		/// </summary>
		public const ushort DefaultTimeout = 5;

		/// <summary>
		/// Special state value indicating a broadcast message.
		/// Is used by only the server when the packet is not a response to a specific client request.
		/// </summary>
		public const ushort Broadcast = ushort.MaxValue;

		/// <summary>
		/// Creates a new Relay instance using the specified <see cref="IConnector"/> type.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public static Relay New<T>() where T : IConnector, new()
			=> new(new T());

		/// <summary>
		/// Creates a new Relay instance with the provided connector.
		/// </summary>
		/// <param name="connector"></param>
		public Relay(IConnector connector) {
			_connector = connector;
			_connector.OnReceived.AddListener(OnReceived);
		}

		private readonly IConnector _connector;
		private          ushort     _nextState = ushort.MinValue + 1;

		/// <summary>
		/// Generates the next state value for the next request.
		/// Wraps around to 0 when reaching <see cref="Broadcast"/>.
		/// </summary>
		/// <returns></returns>
		public ushort NextState() {
			if (_nextState >= Broadcast)
				_nextState = 0;
			return _nextState++;
		}

		private void OnReceived(Buffer buffer)
			=> OnReceivedAsync(buffer).Forget();

		private async UniTask OnReceivedAsync(Buffer buffer) {
			await UniTask.SwitchToThreadPool();
			buffer.Start();

			if (buffer.Remaining < HeaderSize) {
				Logger.LogWarning($"Received buffer with insufficient data: {buffer.Remaining} < {HeaderSize}\n{buffer}");
				return;
			}

			var length = buffer.ReadUShort();
			var state  = buffer.ReadUShort();
			var type   = buffer.ReadEnum<ResponseType>();
			if (length < HeaderSize || length > buffer.Length) {
				Logger.LogWarning($"Received invalid packet length: {length} (expected >= {HeaderSize} and <= {buffer.Length})\n{buffer}");
				return;
			}

			switch (type) {
				case ResponseType.Enter:
				case ResponseType.Quit:
				case ResponseType.Join:
				case ResponseType.Leave:
				case ResponseType.Transform:
				case ResponseType.Custom:
				case ResponseType.AvatarChanged:
				case ResponseType.Properties:
				case ResponseType.PlayerUpdate:
				case ResponseType.Traveling:
				case ResponseType.Teleport:
				case ResponseType.Voice:
				case ResponseType.Event:
					HandleInstance((ushort)(length - HeaderSize), type, state, buffer);
					break;
				case ResponseType.Disconnect:
					HandleDisconnect((ushort)(length - HeaderSize), state, buffer);
					break;
				case ResponseType.Latency:
					HandleLatency((ushort)(length - HeaderSize), state, buffer);
					break;
				case ResponseType.Handshake:
				case ResponseType.Segmentation:
				case ResponseType.Reliable:
				case ResponseType.Authentification:
				case ResponseType.ServerConfig:
				case ResponseType.Rooms:
					break;
				case ResponseType.None:
				case ResponseType.PasswordRequirement:
				default:
					Logger.LogWarning($"Unknown receive type: {type}");
					break;
			}
		}

		private void HandleLatency(ushort length, ushort state, Buffer buffer) {
			var @event = new Types.Latency.LatencyResponse { State = state, Connection = this };
			if (@event.FromBuffer(buffer.Clone(HeaderSize, length))) {
				LastLatency = @event;
			} else Logger.LogWarning($"Failed to parse {nameof(Types.Latency.LatencyResponse)} from buffer", tag: nameof(HandleLatency));
		}

		private void HandleInstance(ushort length, ResponseType type, ushort state, Buffer buffer) {
			var iid      = buffer.ReadByte();
			var instance = Rooms.FirstOrDefault(s => s.InternalId == iid);

			if (instance == null) {
				Logger.LogWarning($"Received packet for unknown instance ID {iid} (type: {type}, state: {state})", tag: nameof(HandleInstance));
				return;
			}

			instance.OnReceived((ushort)(length - 1), state, type, buffer.Clone(HeaderSize + 1, length)).Forget();
		}

		private void HandleDisconnect(ushort length, ushort state, Buffer buffer) {
			var @event = new Types.Disconnect.DisconnectEvent { State = state, Connection = this };
			if (@event.FromBuffer(buffer.Clone(HeaderSize, length))) {
				Logger.Log($"Disconnected by server: {@event.Reason}", tag: nameof(HandleDisconnect));
			} else Logger.LogWarning($"Failed to parse {nameof(Types.Disconnect.DisconnectEvent)} from buffer", tag: nameof(HandleDisconnect));
		}

		public readonly HashSet<Rooms.Room>     Rooms = new();
		public          Types.Handshakes.HandshakeResponse LastHandshake;
		public          Types.Latency.LatencyResponse      LastLatency;

		/// <summary>
		/// Gets the client identifier assigned by the server during the last handshake.
		/// </summary>
		public ushort ClientId
			=> LastHandshake?.ClientId ?? ushort.MaxValue;

		/// <summary>
		/// Gets the last measured ping in milliseconds.
		/// Returns -1 if no latency measurement has been made.
		/// </summary>
		public double Ping
			=> LastLatency?.GetLatency().TotalMilliseconds ?? -1;

		public async UniTask Dispose() {
			if (_connector.IsConnected) {
				await Disconnect();
				await _connector.Close();
			}

			LastHandshake = null;
			LastLatency   = null;
		}

		public struct EmitResult {
			public EmitResult(bool success, ushort state) {
				Success = success;
				State   = state;
			}

			public readonly bool   Success;
			public readonly ushort State;
		}

		/// <summary>
		/// Sends data to the server with the specified request type and state.
		/// Is returns an EmitResult indicating success and the state used.
		/// </summary>
		/// <param name="data"></param>
		/// <param name="type"></param>
		/// <param name="state"></param>
		/// <returns></returns>
		public async UniTask<EmitResult> Emit(
			Buffer      data,
			RequestType type  = RequestType.None,
			ushort      state = ushort.MaxValue) {
			if (!_connector.IsConnected)
				return new EmitResult(
					false,
					ushort.MaxValue
				);

			state = state == ushort.MaxValue
				? NextState()
				: state;

			var buffer = new Buffer();
			buffer.Write((ushort)(data.Length + HeaderSize));
			buffer.Write(state);
			buffer.Write(type);

			// Évite la copie si possible
			buffer.Write(data);

			return new EmitResult(
				await _connector.Send(buffer),
				state
			);
		}

		/// <summary>
		/// Sends a request and waits for a response of type T.
		/// </summary>
		/// <param name="request"></param>
		/// <param name="out"></param>
		/// <param name="in"></param>
		/// <param name="state"></param>
		/// <param name="timeout"></param>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public async UniTask<T> Request<T>(
			ContentRequest request,
			RequestType    @out,
			ResponseType   @in,
			ushort         state,
			ushort         timeout = 0)
			where T : ContentResponse, new() {
			if (!_connector.IsConnected)
				return null;

			if (timeout == 0)
				timeout = LastHandshake?.SegmentationTimeout ?? DefaultTimeout;

			var initTime = DateTime.UtcNow;
			var tcs      = new UniTaskCompletionSource<T>();

			UnityAction<Buffer> handler = OnPacket;

			_connector.OnReceived.AddListener(handler);

			var result = await Emit(request.ToBuffer(), @out, state);
			if (!result.Success) {
				_connector.OnReceived.RemoveListener(handler);
				Logger.LogWarning($"Failed to emit {state}:{@out}", tag: nameof(Request));
				return null;
			}

			// Timeout avec UniTask
			var task  = tcs.Task;
			var delay = UniTask.Delay(TimeSpan.FromSeconds(timeout));
			var (success, response) = await UniTask.WhenAny(task, delay);

			_connector.OnReceived.RemoveListener(handler);

			if (success) {
				response.Time = (initTime, DateTime.UtcNow);
				return response;
			}

			Logger.LogWarning($"{state}:{@out} timeout", tag: nameof(Request));
			return null;

			void OnPacket(Buffer buffer) {
				buffer.Start();
				if (buffer.Remaining < HeaderSize) {
					Logger.LogWarning($"Received buffer with insufficient data: {buffer.Remaining} < {HeaderSize}\n{buffer}");
					return;
				}

				var l = buffer.ReadUShort();
				var s = buffer.ReadUShort();
				var t = buffer.ReadEnum<ResponseType>();

				if (t != @in) return;
				if (state != ushort.MaxValue && s != state) return;

				var r = new T { State = state, Connection = this };
				if (r.FromBuffer(buffer.Clone(HeaderSize, l)))
					tcs.TrySetResult(r);
				else
					Logger.LogError($"Failed to parse {state}:{@in} to {typeof(T)}", tag: nameof(Request));
			}
		}


		/// <summary>
		/// Performs a handshake with the relay server.
		/// This establishes the initial connection parameters and retrieves server information.
		/// </summary>
		/// <returns></returns>
		public async UniTask<Types.Handshakes.HandshakeResponse> Handshake()
			=> LastHandshake = await Request<Types.Handshakes.HandshakeResponse>(
				new Types.Handshakes.HandshakeRequest {
					ProtocolVersion = ProtocolVersion,
					Engine          = EngineExtensions.CurrentEngine,
					Platform        = PlatformExtensions.CurrentPlatform
				},
				RequestType.Handshake,
				ResponseType.Handshake,
				NextState()
			);

		/// <summary>
		/// Sends a latency test request to the server and awaits the response.
		/// This is used to measure the round-trip time and keep the connection alive.
		/// </summary>
		/// <returns></returns>
		public async UniTask<Types.Latency.LatencyResponse> Latency() {
			var req = Types.Latency.LatencyRequest.Now();

			var res = await Request<Types.Latency.LatencyResponse>(
				req,
				RequestType.Latency,
				ResponseType.Latency,
				NextState()
			);

			if (res == null || res.Challenge != req.Challenge) {
				if (res != null)
					Logger.LogWarning($"Latency response challenge mismatch: {res.Challenge} != {req.Challenge}", tag: nameof(Latency));
			} else LastLatency = res;

			return res;
		}

		/// <summary>
		/// Sends a disconnect request to the server with an optional reason.
		/// This informs the server that the client intends to disconnect gracefully.
		/// </summary>
		/// <param name="reason"></param>
		/// <returns></returns>
		public async UniTask<bool> Disconnect(string reason = null)
			=> (await Emit(
				new Types.Disconnect.DisconnectRequest {
					Reason = reason
				}.ToBuffer(),
				RequestType.Disconnect,
				NextState()
			)).Success;

		/// <summary>
		/// Authenticates the client with the relay server using the provided authentication request.
		/// This typically involves sending signature/public key to verify the client's identity.
		/// </summary>
		/// <param name="request"></param>
		/// <returns></returns>
		public async UniTask<Types.Authentication.AuthenticationResponse> Authenticate(Types.Authentication.AuthenticationRequest request)
			=> await Request<Types.Authentication.AuthenticationResponse>(
					request,
					RequestType.Authentification,
					ResponseType.Authentification,
					NextState()
				)
				?? Types.Authentication.AuthenticationResponse
					.CreateUnknown("The request failed or timed out");

		public async UniTask<bool> Connect(string address, ushort port)
			=> await _connector.Connect(address, port);
	}
}