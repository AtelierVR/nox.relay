using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Nox.CCK.Utils;
using Nox.Relay.Core.Connectors;
using Nox.Relay.Core.Rooms;
using Nox.Relay.Core.Types;
using Nox.Relay.Core.Types.Authentication;
using Nox.Relay.Core.Types.Avatars;
using Nox.Relay.Core.Types.Contents;
using Nox.Relay.Core.Types.Latency;
using Nox.Relay.Core.Types.Rooms;
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
		public const ushort DefaultTimeout = 0;

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
			Connector = connector;
			Connector.OnReceived.AddListener(OnReceived);
			Connector.OnConnected.AddListener(OnConnected);
			Connector.OnDisconnected.AddListener(OnDisconnected);
			if (Connector.IsConnected) OnConnected();
		}

		public readonly IConnector Connector;
		private ushort _nextState = ushort.MinValue;
		private CancellationTokenSource _keepAliveCts;

		/// <summary>
		/// Invoked when a buffer is received from the connector.
		/// </summary>
		public readonly UnityEvent<Buffer> OnReceiveBuffer = new();

		/// <summary>
		/// Invoked when a packet is received from the connector.
		/// </summary>
		public readonly UnityEvent<ushort, ResponseType, Buffer> OnReceivePacket = new();

		/// <summary>
		/// Generates the next state value for the next request.
		/// Wraps around to 0 when reaching <see cref="Broadcast"/>.
		/// </summary>
		/// <returns></returns>
		public ushort NextState() {
			if (_nextState >= Broadcast)
				_nextState = ushort.MinValue;
			return _nextState++;
		}

		private void OnReceived(Buffer buffer)
			=> OnReceivedAsync(buffer).Forget();

		private async UniTask OnReceivedAsync(Buffer buffer) {
			await UniTask.SwitchToMainThread();

			buffer.Start();

			if (buffer.Remaining < HeaderSize) {
				Logger.LogWarning(
					$"Received buffer with insufficient data: {buffer.Remaining} < {HeaderSize}\n{buffer}");
				return;
			}

			OnReceiveBuffer.Invoke(buffer.Clone());

			var length = buffer.ReadUShort();
			var state = buffer.ReadUShort();
			var type = buffer.ReadEnum<ResponseType>();
			if (length < HeaderSize || length > buffer.Length) {
				Logger.LogWarning(
					$"Received invalid packet length: {length} (expected >= {HeaderSize} and <= {buffer.Length})\n{buffer}");
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
					HandleInstance(state, type, buffer.Clone(HeaderSize, length));
					break;
				case ResponseType.Disconnect:
					HandleDisconnect(state, buffer.Clone(HeaderSize, length));
					break;
				case ResponseType.Latency:
					HandleLatency(state, buffer.Clone(HeaderSize, length));
					break;
				case ResponseType.Handshake:
					HandleHandshake(state, buffer.Clone(HeaderSize, length));
					break;
				case ResponseType.Segmentation:
				case ResponseType.Reliable:
				case ResponseType.Authentification:
				case ResponseType.ServerConfig:
				case ResponseType.Rooms:
					break;
				case ResponseType.None:
				case ResponseType.PasswordRequirement:
				default:
					Logger.LogWarning($"Unknown receive type: {type}: {buffer}");
					break;
			}

			OnReceivePacket.Invoke(state, type, buffer.Clone(HeaderSize, length));
		}

		private void HandleHandshake(ushort state, Buffer buffer) {
			buffer.Start();
			var @event = new Types.Handshakes.HandshakeResponse { State = state, Connection = this };
			if (!@event.FromBuffer(buffer)) return;
			LastHandshake = @event;
			if (LastHandshake.Protocol != ProtocolVersion)
				Logger.LogWarning(
					$"Protocol version mismatch: server={LastHandshake.Protocol}, client={ProtocolVersion}",
					tag: nameof(Relay));
			if (LastHandshake.MaxPacketSize > 0)
				Connector.Mtu = LastHandshake.MaxPacketSize;
		}

		private void HandleLatency(ushort state, Buffer buffer) {
			buffer.Start();
			var @event = new LatencyResponse { State = state, Connection = this };
			if (!@event.FromBuffer(buffer)) return;
			LastLatency = @event;
		}

		private void HandleInstance(ushort state, ResponseType type, Buffer buffer) {
			buffer.Start();
			var iid = buffer.ReadByte();
			var instance = Rooms.FirstOrDefault(s => s.InternalId == iid);

			if (instance == null) {
				Logger.LogWarning($"Received packet for unknown instance ID {iid} (type: {type}, state: {state})", tag: nameof(Relay));
				return;
			}

			instance.OnReceived(state, type, buffer.Clone(1, buffer.Length));
		}

		private void HandleDisconnect(ushort state, Buffer buffer) {
			buffer.Start();
			var @event = new Types.Disconnect.DisconnectEvent { State = state, Connection = this };
			if (!@event.FromBuffer(buffer)) return;
			Logger.Log($"Disconnected by server: {@event.Reason}", tag: nameof(Relay));
		}

		public readonly HashSet<Room> Rooms = new();
		public Types.Handshakes.HandshakeResponse LastHandshake;
		public LatencyResponse LastLatency;

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

		/// <summary>
		/// Gets the current server time based on the last latency measurement.
		/// If no latency measurement has been made, returns DateTime.MinValue.
		/// </summary>
		public DateTime Time {
			get {
				if (LastLatency == null)
					return DateTime.MinValue;
				var server = LastLatency.IntermediateTime;
				var local = LastLatency.InitialTime;
				var now = DateTime.UtcNow;
				var diff = now - local;
				return server + diff;
			}
		}

		/// <summary>
		/// Starts the keep-alive loop that sends periodic latency requests to maintain the connection.
		/// The interval is determined by the KeepAliveInterval from the last handshake.
		/// </summary>
		private void StartKeepAlive() {
			StopKeepAlive();
			_keepAliveCts = new CancellationTokenSource();
			KeepAliveLoop(_keepAliveCts.Token).Forget();
		}

		/// <summary>
		/// Stops the keep-alive loop.
		/// </summary>
		private void StopKeepAlive() {
			if (_keepAliveCts == null) return;
			_keepAliveCts.Cancel();
			_keepAliveCts.Dispose();
			_keepAliveCts = null;
		}

		/// <summary>
		/// Keep-alive loop that periodically sends latency requests.
		/// </summary>
		/// <param name="cancellationToken"></param>
		private async UniTaskVoid KeepAliveLoop(CancellationToken cancellationToken) {
			try {
				while (!cancellationToken.IsCancellationRequested) {
					var interval = LastHandshake?.KeepAliveInterval ?? 5;
					await UniTask.Delay(TimeSpan.FromSeconds(interval), cancellationToken: cancellationToken);
					if (cancellationToken.IsCancellationRequested)
						break;
					await Latency();
				}
			} catch (OperationCanceledException) {
				// Normal cancellation, do nothing
			} catch (Exception ex) {
				Logger.LogError(new Exception("Error in keep-alive loop", ex), tag: nameof(Relay));
			}
		}

		public async UniTask Dispose() {
			if (Connector.IsConnected) {
				await Disconnect();
				await Connector.Close();
			}

			LastHandshake = null;
			LastLatency = null;
		}

		public struct EmitResult {
			public EmitResult(bool success, ushort state) {
				Success = success;
				State = state;
			}

			public readonly bool Success;
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
			Buffer data,
			RequestType type = RequestType.None,
			ushort state = Broadcast) {
			if (!Connector.IsConnected)
				return new EmitResult(
					false,
					state
				);

			state = state == Broadcast
				? NextState()
				: state;

			var buffer = new Buffer();
			buffer.Write((ushort)(data.Length + HeaderSize));
			buffer.Write(state);
			buffer.Write(type);

			// Évite la copie si possible
			buffer.Write(data);

			return new EmitResult(
				await Connector.Send(buffer),
				state
			);
		}

		public struct ValidateInput<T> where T : ContentResponse, new() {
			public ushort expectedState;
			public ResponseType expectedType;

			public ushort receivedState;
			public ResponseType receivedType;
			public Buffer payload;

			public T response;
		}

		private bool OnValidatePacket<T>(ValidateInput<T> input) where T : ContentResponse, new() {
			return input.response.FromBuffer(input.payload);
		}

		/// <summary>
		/// Sends a request and waits for a response of type T.
		/// </summary>
		/// <param name="request"></param>
		/// <param name="out"></param>
		/// <param name="in"></param>
		/// <param name="state"></param>
		/// <param name="timeout"></param>
		/// <param name="emitter"></param>
		/// <param name="validate"></param>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public async UniTask<T> Request<T>(
			ContentRequest request,
			RequestType @out,
			ResponseType @in,
			ushort state = Broadcast,
			ushort timeout = DefaultTimeout,
			Func<Buffer, RequestType, ushort, UniTask<EmitResult>> emitter = null,
			Func<ValidateInput<T>, bool> validate = null
		) where T : ContentResponse, new() {
			if (!Connector.IsConnected)
				return null;

			if (timeout == 0)
				timeout = LastHandshake?.SegmentationTimeout ?? 15;

			emitter ??= Emit;
			validate ??= OnValidatePacket;

			var initTime = DateTime.UtcNow;
			var tcs = new UniTaskCompletionSource<T>();

			// Store the actual state in a boxed container to avoid capture issues
			var stateContainer = new ushort[1];

			UnityAction<ushort, ResponseType, Buffer> handler = OnPacket;

			OnReceivePacket.AddListener(handler);

			var result = await emitter(request.ToBuffer(), @out, state);
			if (!result.Success) {
				OnReceivePacket.RemoveListener(handler);
				Logger.LogWarning($"Failed to emit {result.State}:{@out}", tag: nameof(Relay));
				return null;
			}

			// Store the actual state returned by emitter
			stateContainer[0] = result.State;

			// Timeout avec UniTask
			var task = tcs.Task;
			var delay = UniTask.Delay(TimeSpan.FromSeconds(timeout));
			var (success, response) = await UniTask.WhenAny(task, delay);

			OnReceivePacket.RemoveListener(handler);

			if (success) {
				response.Time = (initTime, DateTime.UtcNow);
				return response;
			}

			Logger.LogWarning($"{stateContainer[0]}:{@out} timeout", tag: nameof(Relay));
			return null;

			void OnPacket(ushort s, ResponseType t, Buffer payload) {
				var expectedState = stateContainer[0];

				if (t != @in) return;
				if (expectedState != ushort.MaxValue && s != expectedState) return;

				var input = new ValidateInput<T> {
					response = new T { State = expectedState, Connection = this },
					expectedState = stateContainer[0],
					expectedType = @in,
					receivedState = s,
					receivedType = t,
					payload = payload,
				};

				if (validate(input))
					tcs.TrySetResult(input.response);
				else Logger.LogError($"Failed to parse {expectedState}:{@in} to {typeof(T)}", tag: nameof(Relay));
			}
		}


		/// <summary>
		/// Performs a handshake with the relay server.
		/// This establishes the initial connection parameters and retrieves server information.
		/// </summary>
		/// <returns></returns>
		public async UniTask<Types.Handshakes.HandshakeResponse> Handshake()
			=> await Request<Types.Handshakes.HandshakeResponse>(
				new Types.Handshakes.HandshakeRequest {
					ProtocolVersion = ProtocolVersion,
					Engine = EngineExtensions.CurrentEngine,
					Platform = PlatformExtensions.CurrentPlatform
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
		public async UniTask<LatencyResponse> Latency() {
			var req = LatencyRequest.Now();

			var res = await Request<LatencyResponse>(
				req,
				RequestType.Latency,
				ResponseType.Latency,
				NextState()
			) ?? LatencyResponse.Failed();

			if (res.Challenge != req.Challenge)
				Logger.LogWarning($"Latency response challenge mismatch: {res.Challenge} != {req.Challenge}", tag: nameof(Relay));

			else LastLatency = res;

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
		public async UniTask<AuthenticationResponse> Authenticate(
			AuthenticationRequest request)
			=> await Request<AuthenticationResponse>(
					request,
					RequestType.Authentification,
					ResponseType.Authentification,
					NextState()
				)
				?? AuthenticationResponse
					.CreateUnknown("The request failed or timed out");

		/// <summary>
		/// Lists available rooms from the relay server for the specified page.
		/// </summary>
		/// <param name="page"></param>
		/// <returns></returns>
		public async UniTask<RoomsResponse> List(byte page) {
			var sessions = await Request<RoomsResponse>(
				new RoomsRequest { Page = page },
				RequestType.Rooms,
				ResponseType.Rooms,
				NextState()
			);
			
			if (sessions == null) 
				return null;

			foreach (var room in sessions.Rooms) {
				room.Connection = this;
				if (Rooms.All(s => s.InternalId != room.InternalId))
					Rooms.Add(room);
			}

			return sessions;
		}

		/// <summary>
		/// Lists all available rooms from the relay server by retrieving all pages.
		/// </summary>
		/// <returns></returns>
		public async UniTask<RoomsResponse> List() {
			var all = await List(0);
			if (all == null) return null;

			var l = all.Rooms.ToList();
			for (byte i = 1; i < all.PageCount; i++) {
				var next = await List(i);
				if (next == null) break;
				l.AddRange(next.Rooms);
			}

			all.Rooms = l.ToArray();
			return all;
		}

		/// <summary>
		/// Lists a specific room by its node ID by searching through all available rooms.
		/// </summary>
		/// <param name="mid"></param>
		/// <returns></returns>
		public async UniTask<Room> List(uint mid) {
			byte total, page = 0;
			Room room = null;

			do {
				var sessions = await List(page++);
				if (sessions == null) return null;
				total = sessions.PageCount;
				foreach (var inst in sessions.Rooms) {
					if (inst.NodeId != mid) continue;
					room = inst;
					break;
				}
			}
			while (room == null && page < total);

			return room;
		}

		public async UniTask<bool> Connect(string address, ushort port)
			=> await Connector.Connect(address, port);

		private void OnConnected()
			=> StartKeepAlive();

		private void OnDisconnected(string message)
			=> StopKeepAlive();

		public override string ToString()
			=> $"{GetType().Name}<{Connector.GetType().Name}>[Connected={Connector.IsConnected}, ClientId={ClientId}]";
	}
}