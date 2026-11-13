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
		public const ushort PROTOCOL_VERSION = 5;

		/// <summary>
		/// Size of the relay packet header in bytes.
		/// Consists of:
		/// - Length <see cref="ushort"/> (2 bytes)
		/// - State  <see cref="ushort"/> (2 bytes)
		/// - Type   <see cref="PacketType"/> (1 byte)
		/// </summary>
		public const int HEADER_SIZE = 5;

		/// <summary>
		/// Default timeout duration in seconds for relay requests.
		/// </summary>
		public const ushort DEFAULT_TIMEOUT = 0;

		/// <summary>
		/// Timeout of the datagram latency probe, in seconds. Short on purpose: a lost datagram falls
		/// back to a stream instead of stalling the keep-alive until the connection timeout.
		/// </summary>
		private const ushort LATENCY_DATAGRAM_TIMEOUT = 2;

		/// <summary>
		/// Special state value indicating a broadcast message.
		/// Is used by only the server when the packet is not a response to a specific client request.
		/// </summary>
		public const ushort BROADCAST = ushort.MaxValue;

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
			if (Connector.IsConnected)
				OnConnected(true);
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
		public readonly UnityEvent<ushort, PacketType, Buffer> OnReceivePacket = new();

		/// <summary>
		/// Generates the next state value for the next request.
		/// Wraps around to 0 when reaching <see cref="BROADCAST"/>.
		/// </summary>
		/// <returns></returns>
		public ushort NextState() {
			if (_nextState >= BROADCAST)
				_nextState = ushort.MinValue;
			return _nextState++;
		}

		private DateTime _receivedAt;

		private void OnReceived(ReceivedPacket packet)
			=> OnReceivedAsync(packet).Forget();

		private async UniTask OnReceivedAsync(ReceivedPacket packet) {
			await UniTask.SwitchToMainThread();

			_receivedAt = packet.At;

			var buffer = packet.Buffer;
			buffer.Start();

			if (buffer.Remaining < HEADER_SIZE) {
				Logger.LogWarning(
					$"Received buffer with insufficient data: {buffer.Remaining} < {HEADER_SIZE}\n{buffer}");
				return;
			}

			OnReceiveBuffer.Invoke(buffer.Clone());

			var length = buffer.ReadUShort();
			var state  = buffer.ReadUShort();
			var type   = buffer.ReadEnum<PacketType>();
			if (length < HEADER_SIZE || length > buffer.Length) {
				Logger.LogWarning(
					$"Received invalid packet length: {length} (expected >= {HEADER_SIZE} and <= {buffer.Length})\n{buffer}");
				return;
			}

			switch (type) {
				case PacketType.Enter:
				case PacketType.Quit:
				case PacketType.Join:
				case PacketType.Leave:
				case PacketType.Transform:
				case PacketType.Custom:
				case PacketType.AvatarChanged:
				case PacketType.Properties:
				case PacketType.PlayerUpdate:
				case PacketType.ServerConfig:
				case PacketType.Traveling:
				case PacketType.Teleport:
				case PacketType.Stream:
				case PacketType.Event:
				case PacketType.Entities:
					HandleInstance(state, type, buffer.Clone(HEADER_SIZE, length));
					break;
				case PacketType.Disconnect:
					HandleDisconnect(state, buffer.Clone(HEADER_SIZE, length));
					break;
				case PacketType.Latency:
					HandleLatency(state, buffer.Clone(HEADER_SIZE, length), packet);
					break;
				case PacketType.Handshake:
					HandleHandshake(state, buffer.Clone(HEADER_SIZE, length));
					break;
				case PacketType.Message:
					HandleMessage(state, buffer.Clone(HEADER_SIZE, length));
					break;
				case PacketType.Transfer:
					HandleTransfer(state, buffer.Clone(HEADER_SIZE, length));
					break;
				case PacketType.Reliable:
				case PacketType.Authentication:
				case PacketType.Sessions:
					break;
				case PacketType.None:
				case PacketType.PasswordRequirement:
				default:
					Logger.LogWarning($"Unknown receive type: {type}: {buffer}");
					break;
			}

			OnReceivePacket.Invoke(state, type, buffer.Clone(HEADER_SIZE, length));
		}

		private void HandleMessage(ushort state, Buffer buffer) {
			buffer.Start();
			var message = buffer.ReadString();
			Logger.Log($"Received message from server: {message}", tag: nameof(Relay));
		}

		private void HandleTransfer(ushort state, Buffer buffer) {
			buffer.Start();
			var @event = new Types.Transfer.TransferEvent { State = state, Connection = this };
			if (!@event.FromBuffer(buffer))
				return;
			Logger.Log(
				@event.IsRelayChange
					? $"Server requested transfer to {@event.Address} ({@event.Reason})"
					: $"Server requested move to room {@event.Room} ({@event.Reason})",
				tag: nameof(Relay));
			OnTransfer.Invoke(@event);
		}
		private void HandleHandshake(ushort state, Buffer buffer) {
			buffer.Start();
			var @event = new Types.Handshakes.HandshakeResponse { State = state, Connection = this };
			if (!@event.FromBuffer(buffer))
				return;
			LastHandshake = @event;
			if (LastHandshake.Protocol != PROTOCOL_VERSION)
				Logger.LogWarning(
					$"Protocol version mismatch: server={LastHandshake.Protocol}, client={PROTOCOL_VERSION}",
					tag: nameof(Relay));
			if (LastHandshake.MaxPacketSize > 0)
				Connector.Mtu = LastHandshake.MaxPacketSize;
		}

		private void HandleLatency(ushort state, Buffer buffer, ReceivedPacket packet) {
			buffer.Start();
			var @event = new LatencyResponse { State = state, Connection = this };
			if (!@event.FromBuffer(buffer))
				return;
			if (packet.At != default)
				@event.ReceivedAt = packet.At;

			LastLatency = @event;
			OnLatency.Invoke(LastLatency);
		}

		private void HandleInstance(ushort state, PacketType type, Buffer buffer) {
			buffer.Start();
			var iid      = buffer.ReadByte();
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
			if (!@event.FromBuffer(buffer))
				return;
			Logger.Log($"Disconnected by server: {@event.Reason}", tag: nameof(Relay));
		}

		public readonly HashSet<Room> Rooms = new();
		public Types.Handshakes.HandshakeResponse LastHandshake;
		public LatencyResponse LastLatency;

		public UnityEvent<LatencyResponse> OnLatency { get; } = new();

		/// <summary>
		/// Raised when the relay asks this client to move to another relay.
		/// The session layer handles the reconnection, but the event lets mods react (UI, logs, metrics).
		/// </summary>
		public UnityEvent<Types.Transfer.TransferEvent> OnTransfer { get; } = new();

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
				var local  = LastLatency.InitialTime;
				var now    = Clock.UtcNow;
				var diff   = now - local;
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
			if (_keepAliveCts == null)
				return;
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
					await Latency();

					var interval = LastHandshake?.KeepAliveInterval ?? 5;
					await UniTask.Delay(TimeSpan.FromSeconds(interval), cancellationToken: cancellationToken);
				}
			} catch (OperationCanceledException) {
				// Normal cancellation, do nothing
			} catch (Exception ex) {
				Logger.LogError(new Exception("Error in keep-alive loop", ex), tag: nameof(Relay));
			}
		}

		public async UniTask Dispose() {
			StopKeepAlive();

			if (Connector.IsConnected)
				await Disconnect();

			// Always dispose the connector, regardless of connection state.
			// If IsConnected was already false (server-side close, transport error,
			// etc.) the old guard skipped Connector.Dispose() entirely, leaving
			// QuicRegistration alive. The GC would later finalize it and call
			// MsQuicClose while MsQuic worker threads were still running → crash.
			await Connector.Dispose();

			LastHandshake = null;
			LastLatency   = null;
		}

		public struct EmitResult {
			public EmitResult(bool success, ushort state) {
				Success = success;
				State   = state;
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
			Buffer      data,
			PacketType type  = PacketType.None,
			ushort      state = BROADCAST,
			SendType    send  = SendType.Auto
		) {
			if (!Connector.IsConnected)
				return new EmitResult(
					false,
					state
				);

			state = state == BROADCAST
				? NextState()
				: state;

			var buffer = new Buffer();
			buffer.Write((ushort)(data.Length + HEADER_SIZE));
			buffer.Write(state);
			buffer.Write(type);

			// Évite la copie si possible
			buffer.Write(data);

			return new EmitResult(
				await Connector.Send(buffer, send),
				state
			);
		}

		public struct ValidateInput<T> where T : ContentResponse, new() {
			public ushort expectedState;
			public PacketType expectedType;

			public ushort receivedState;
			public PacketType receivedType;
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
		/// <param name="send"></param>
		/// <param name="timeout"></param>
		/// <param name="emitter"></param>
		/// <param name="validate"></param>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public async UniTask<T> Request<T>(
			ContentRequest                                                   request,
			PacketType                                                      @out,
			PacketType                                                     @in,
			ushort                                                           state    = BROADCAST,
			SendType                                                         send     = SendType.Auto,
			ushort                                                           timeout  = DEFAULT_TIMEOUT,
			Func<Buffer, PacketType, ushort, SendType, UniTask<EmitResult>> emitter  = null,
			Func<ValidateInput<T>, bool>                                     validate = null,
			CancellationToken                                                token    = default
		) where T : ContentResponse, new() {
			token.ThrowIfCancellationRequested();

			if (!Connector.IsConnected)
				return null;

			if (timeout == 0)
				timeout = LastHandshake?.ConnectionTimeout ?? 15;

			emitter  ??= Emit;
			validate ??= OnValidatePacket;

			var initTime = Clock.UtcNow;
			var tcs      = new UniTaskCompletionSource<T>();

			// Store the actual state in a boxed container to avoid capture issues
			var stateContainer = new ushort[ 1 ];

			UnityAction<ushort, PacketType, Buffer> handler = OnPacket;

			OnReceivePacket.AddListener(handler);

			try {
				var result = await emitter(request.ToBuffer(), @out, state, send);
				if (!result.Success) {
					Logger.LogWarning($"Failed to emit {result.State}:{@out}", tag: nameof(Relay));
					return null;
				}

				// Store the actual state returned by emitter
				stateContainer[0] = result.State;

				// Timeout avec UniTask
				var task  = tcs.Task;
				var delay = UniTask.Delay(TimeSpan.FromSeconds(timeout), cancellationToken: token);
				var (success, response) = await UniTask.WhenAny(task, delay);

				token.ThrowIfCancellationRequested();

				if (success) {
					response.Time = (initTime, Clock.UtcNow);
					return response;
				}

				Logger.LogWarning($"{stateContainer[0]}:{@out} timeout", tag: nameof(Relay));
				return null;
			} finally {
				OnReceivePacket.RemoveListener(handler);
			}

			void OnPacket(ushort s, PacketType t, Buffer payload) {
				var expectedState = stateContainer[0];

				if (t != @in)
					return;
				if (expectedState != ushort.MaxValue && s != expectedState)
					return;

				var input = new ValidateInput<T> {
					response      = new T { State = expectedState, Connection = this },
					expectedState = stateContainer[0],
					expectedType  = @in,
					receivedState = s,
					receivedType  = t,
					payload       = payload,
				};

				input.response.ReceivedAt = _receivedAt;

				if (validate(input))
					tcs.TrySetResult(input.response);
				else
					Logger.LogError($"Failed to parse {expectedState}:{@in} to {typeof(T)}", tag: nameof(Relay));
			}
		}


		/// <summary>
		/// Performs a handshake with the relay server.
		/// This establishes the initial connection parameters and retrieves server information.
		/// </summary>
		/// <param name="transferToken">
		/// Optional opaque, client-unique transfer token received from a
		/// <see cref="Types.Transfer.TransferEvent"/>. When the relay can resolve it the client is
		/// authenticated immediately and the response carries
		/// <see cref="Types.Handshakes.HandshakeFlags.Transferred"/>, so no challenge-response is needed.
		/// </param>
		/// <returns></returns>
		public async UniTask<Types.Handshakes.HandshakeResponse> Handshake(
			CancellationToken token = default,
			byte[] transferToken = null
		)
			=> await Request<Types.Handshakes.HandshakeResponse>(
				new Types.Handshakes.HandshakeRequest {
					ProtocolVersion = PROTOCOL_VERSION,
					Endpoint        = Connector.EndPoint,
					Engine          = EngineExtensions.CurrentEngine,
					Platform        = PlatformExtensions.CurrentPlatform,
					TransferToken   = transferToken
				},
				PacketType.Handshake,
				PacketType.Handshake,
				NextState(),
				token: token
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
				PacketType.Latency,
				PacketType.Latency,
				NextState(),
				SendType.Datagram,
				LATENCY_DATAGRAM_TIMEOUT
			);

			if (res == null) {
				req = LatencyRequest.Now();
				res = await Request<LatencyResponse>(
					req,
					PacketType.Latency,
					PacketType.Latency,
					NextState()
				);
			}

			if (res == null)
				return LatencyResponse.Failed();

			if (res.Challenge != req.Challenge)
				Logger.LogWarning($"Latency response challenge mismatch: {res.Challenge} != {req.Challenge}", tag: nameof(Relay));

			else
				LastLatency = res;

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
				PacketType.Disconnect,
				NextState()
			)).Success;

		/// <summary>
		/// Authenticates the client with the relay server using the provided authentication request.
		/// This typically involves sending signature/public key to verify the client's identity.
		/// </summary>
		/// <param name="request"></param>
		/// <returns></returns>
		public async UniTask<AuthenticationResponse> Authenticate(
			AuthenticationRequest request,
			CancellationToken     token   = default
		)
			=> await Request<AuthenticationResponse>(
					request,
					PacketType.Authentication,
					PacketType.Authentication,
					NextState(),
					token: token
				)
				?? AuthenticationResponse
					.CreateUnknown("The request failed or timed out");

		/// <summary>
		/// Lists available rooms from the relay server for the specified page.
		/// </summary>
		/// <param name="page"></param>
		/// <returns></returns>
		public async UniTask<RoomsResponse> List(byte page, CancellationToken token = default) {
			var sessions = await Request<RoomsResponse>(
				new RoomsRequest { Page = page },
				PacketType.Sessions,
				PacketType.Sessions,
				NextState(),
				token: token
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
			if (all == null)
				return null;

			var l = all.Rooms.ToList();
			for (byte i = 1; i < all.PageCount; i++) {
				var next = await List(i);
				if (next == null)
					break;
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
		public async UniTask<Room> List(uint mid, CancellationToken token = default) {
			token.ThrowIfCancellationRequested();

			byte total,
				page = 0;
			Room room = null;

			do {
				var sessions = await List(page++, token);
				if (sessions == null)
					return null;
				total = sessions.PageCount;
				foreach (var inst in sessions.Rooms) {
					if (inst.NodeId != mid)
						continue;
					room = inst;
					break;
				}
			} while (room == null && page < total);

			return room;
		}

		/// <summary>
		/// Finds a room by its relay-local id (<see cref="Room.InternalId"/>), paging through the room
		/// list. Used for server-initiated moves and auto-enter.
		/// </summary>
		/// <param name="roomId">Room id, as listed by the relay.</param>
		public async UniTask<Room> Find(byte roomId, CancellationToken token = default) {
			token.ThrowIfCancellationRequested();

			byte total,
				page = 0;
			Room room = null;

			do {
				var sessions = await List(page++, token);
				if (sessions == null)
					return null;
				total = sessions.PageCount;
				foreach (var inst in sessions.Rooms) {
					if (inst.InternalId != roomId)
						continue;
					room = inst;
					break;
				}
			} while (room == null && page < total);

			return room;
		}

		public async UniTask<bool> Connect(string address, ushort port, CancellationToken token = default)
			=> await Connector.Connect(address, port, token);

		private void OnConnected(bool succes) {
			if (!succes)
				return;
			StartKeepAlive();
		}

		private void OnDisconnected(string message)
			=> StopKeepAlive();

		public override string ToString()
			=> $"{GetType().Name}<{Connector.GetType().Name}>[Connected={Connector.IsConnected}, ClientId={ClientId}]";
	}
}
