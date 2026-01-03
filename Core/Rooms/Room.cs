using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Nox.CCK.Utils;
using Nox.Relay.Core.Players;
using Nox.Relay.Core.Types;
using Nox.Relay.Core.Types.Content.Rooms;
using Nox.Relay.Core.Types.Contents.Rooms;
using Nox.Relay.Core.Types.Enter;
using Nox.Relay.Core.Types.Event;
using Nox.Relay.Core.Types.Properties;
using Nox.Relay.Core.Types.Quit;
using Nox.Relay.Core.Types.Rooms;
using Nox.Relay.Core.Types.Transform;
using Nox.Relay.Core.Types.Traveling;
using UnityEngine.Events;
using Buffer = Nox.CCK.Utils.Buffer;

namespace Nox.Relay.Core.Rooms {
    /// <summary>
    /// Represents a relay room with its associated properties.
    /// A room is hosted on a specific relay and can accommodate multiple players.
    /// </summary>
    public class Room {
        /// <summary>
        /// Unique identifier to the node hosting this room.
        /// The address is defined in the <see cref="Nox.Relay.Core.Types.Handshakes.HandshakeResponse"/> structure.
        /// </summary>
        public uint NodeId;

        /// <summary>
        /// Is the unique id of the room assigned internally by the relay server.
        /// This ID is used to reference for requests and responses related to this room.
        /// </summary>
        public byte InternalId;

        /// <summary>
        /// Current number of players connected to this room.
        /// Used to determine if the room is full or has available slots for new players.
        /// </summary>
        public ushort PlayerCount;

        /// <summary>
        /// Maximum number of players that can join this room.
        /// <see cref="RoomFlags.AllowOverload"/> can allow more players than this value.
        /// </summary>
        public ushort MaxPlayerCount;

        /// <summary>
        /// Flags representing various properties and behaviors of the room.
        /// These flags can indicate if the room is private,
        /// allows overload, or has other special conditions.
        /// </summary>
        public RoomFlags Flags;

        /// <summary>
        /// Current ticks per second (TPS) rate of the room.
        /// This value indicates the performance the recurrent update rate of the room.
        /// A lower TPS can indicate a laggy or overloaded room.
        /// </summary>
        public byte Tps = byte.MaxValue;

        /// <summary>
        /// Threshold for updating entity transforms in the room.
        /// This value defines the minimum change in position or rotation
        /// required to trigger a transform update event.
        /// </summary>
        public float Threshold = float.Epsilon;

        /// <summary>
        /// Maximum distance at which entities are rendered in the room.
        /// Entities beyond this distance from the player will not be rendered.
        /// This setting helps optimize performance by limiting the number of entities drawn.
        /// </summary>
        public float RenderEntity = float.MaxValue;

        /// <summary>
        /// The relay connection associated with this room.
        /// This connection is used to send and receive data related to this room.
        /// </summary>
        public Relay Connection;

        /// <summary>
        /// Set of players currently connected to this room.
        /// This set is updated when players join or leave the room.
        /// </summary>
        public readonly HashSet<Player> Players = new();

        /// <summary>
        /// Event invoked when a traveling event is received in the room.
        /// </summary>
        public UnityEvent<TravelingEvent> OnTraveling { get; } = new();

        /// <summary>
        /// Event invoked when a player joins the room.
        /// </summary>
        public UnityEvent<Types.Join.JoinEvent> OnJoined { get; } = new();

        /// <summary>
        /// Event invoked when a player leaves the room.
        /// </summary>
        public UnityEvent<Types.Leave.LeaveEvent> OnLeft { get; } = new();

        /// <summary>
        /// Event invoked when the local player quites the room.
        /// </summary>
        public UnityEvent<QuitEvent> OnQuited { get; } = new();

        /// <summary>
        /// Event invoked when the local player enters the room.
        /// </summary>
        public UnityEvent<EnterResponse> OnEntered { get; } = new();

        /// <summary>
        /// Event invoked when a transform event is received in the room.
        /// </summary>
        public UnityEvent<Types.Transform.TransformEvent> OnTransform { get; } = new();

        /// <summary>
        /// Event invoked when a custom event is received in the room.
        /// </summary>
        public UnityEvent<Types.Event.EventEvent> OnEvent { get; } = new();

        /// <summary>
        /// Event invoked when a properties event is received in the room.
        /// </summary>
        public UnityEvent<Types.Properties.PropertiesEvent> OnProperties { get; } = new();

        /// <summary>
        /// Handles the reception of data for this relay room.
        /// Is called by <see cref="Relay.HandleInstance"/>, when data is received for this room.
        /// </summary>
        /// <param name="state"></param>
        /// <param name="type"></param>
        /// <param name="buffer"></param>
        internal void OnReceived(ushort state, ResponseType type, Buffer buffer) {
            buffer.Start();

            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (type) {
                case ResponseType.Quit:
                    HandleQuit(state, buffer);
                    break;
                case ResponseType.Enter:
                    HandleEnter(state, buffer);
                    break;
                case ResponseType.Join:
                    HandleJoin(state, buffer);
                    break;
                case ResponseType.Leave:
                    HandleLeave(state, buffer);
                    break;
                case ResponseType.Transform:
                    HandleTransform(state, buffer);
                    break;
                case ResponseType.Event:
                    HandleEvent(state, buffer);
                    break;
                case ResponseType.Properties:
                    HandleProperties(state, buffer);
                    break;
                case ResponseType.Custom:
                case ResponseType.AvatarChanged:
                case ResponseType.PlayerUpdate:
                case ResponseType.Traveling:
                case ResponseType.Teleport:
                case ResponseType.Voice:
                    break;
                default:
                    Logger.LogWarning($"[] Unhandled room response type: {type} (state: {state}) {buffer}", tag: Tag);
                    break;
            }
        }

        private string Tag
            => $"Room:{InternalId}";

        private void HandleEvent(ushort state, Buffer buffer) {
            var evt = new Types.Event.EventEvent { State = state, Connection = Connection, Room = this };
            if (!evt.FromBuffer(buffer)) {
                Logger.LogWarning("Failed to parse EventEvent", tag: Tag);
                return;
            }

            OnEvent.Invoke(evt);
        }

        private void HandleProperties(ushort state, Buffer buffer) {
            var properties = new Types.Properties.PropertiesEvent
                { State = state, Connection = Connection, Room = this };
            if (!properties.FromBuffer(buffer)) {
                Logger.LogWarning("Failed to parse PropertiesEvent", tag: Tag);
                return;
            }

            OnProperties.Invoke(properties);
        }

        private void HandleTransform(ushort state, Buffer buffer) {
            var transform = new Types.Transform.TransformEvent { State = state, Connection = Connection, Room = this };
            if (!transform.FromBuffer(buffer)) {
                Logger.LogWarning("Failed to parse TransformEvent", tag: Tag);
                return;
            }

            OnTransform.Invoke(transform);
        }


        private void HandleQuit(ushort state, Buffer buffer) {
            var quit = new QuitEvent { State = state, Connection = Connection, Room = this };
            if (!quit.FromBuffer(buffer)) {
                Logger.LogWarning("Failed to parse QuitEvent", tag: Tag);
                return;
            }

            OnQuited.Invoke(quit);
            Players.Clear();
            Connection.Rooms.Remove(this);
        }

        private void HandleEnter(ushort state, Buffer buffer) {
            var enter = new EnterResponse { State = state, Connection = Connection, Room = this };
            if (!enter.FromBuffer(buffer)) {
                Logger.LogWarning("Failed to parse EnterResponse", tag: Tag);
                return;
            }

            if (enter.IsError)
                return;

            OnEntered.Invoke(enter);

            Players.Add(enter.Player);
            Connection.Rooms.Add(this);
        }

        private void HandleJoin(ushort state, Buffer buffer) {
            var join = new Types.Join.JoinEvent { State = state, Connection = Connection, Room = this };
            if (!join.FromBuffer(buffer)) {
                Logger.LogWarning("Failed to parse JoinEvent", tag: Tag);
                return;
            }

            OnJoined.Invoke(join);
            Players.Add(join.Player);
        }

        private void HandleLeave(ushort state, Buffer buffer) {
            var leave = new Types.Leave.LeaveEvent { State = state, Connection = Connection, Room = this };
            if (!leave.FromBuffer(buffer)) {
                Logger.LogWarning("Failed to parse LeaveEvent", tag: Tag);
                return;
            }

            OnLeft.Invoke(leave);
            Players.RemoveWhere(p => p.Id == leave.PlayerId);
        }


        /// <summary>
        /// Sends an enter request to join this room.
        /// On success, the local player is added to the room's player set.
        /// Note: The actual addition is handled by HandleEnter() when the response is received.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public async UniTask<EnterResponse> Enter(EnterRequest request) {
            var enter = await Request<EnterResponse>(
                request,
                RequestType.Enter,
                ResponseType.Enter,
                Connection.NextState()
            );

            enter ??= EnterResponse.Unknown(this, "Unknown enter request");

            // Note: Players.Add() and Connection.Rooms.Add() are handled by HandleEnter()
            // when the Enter response is received via OnReceived()

            return enter;
        }

        /// <summary>
        /// Sends a quit request to leave this room.
        /// On success, the local player is removed from the room's player set.
        /// Note: The actual cleanup is handled by HandleQuit() when the response is received.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="reason"></param>
        /// <returns></returns>
        public async UniTask<QuitEvent> Quit(QuitType type = QuitType.Normal, string reason = null) {
            var quit = await Request<QuitEvent>(
                new QuitRequest {
                    Type = type,
                    Reason = reason
                },
                RequestType.Quit,
                ResponseType.Quit,
                Connection.NextState()
            );

            quit ??= QuitEvent.Unknown(this, "Unknown quit request");

            // Note: Players.Clear() and Connection.Rooms.Remove() are handled by HandleQuit()
            // when the Quit response is received via OnReceived()

            return quit;
        }

        /// <summary>
        /// Sends a properties request to set or clear properties for an entity in this room.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public async UniTask<bool> Properties(PropertiesRequest request)
            => (await Emit(
                request.ToBuffer(),
                RequestType.Properties,
                Connection.NextState()
            )).Success;

        /// <summary>
        /// Sends a transform request to update the transform of an object/entity in this room.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public async UniTask<bool> Transform(TransformRequest request)
            => (await Emit(
                request.ToBuffer(),
                RequestType.Transform,
                Connection.NextState()
            )).Success;

        /// <summary>
        /// Sends an event request a event in this room.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public async UniTask<bool> Event(EventRequest request)
            => (await Emit(
                request.ToBuffer(),
                RequestType.Event,
                Connection.NextState()
            )).Success;

        /// <summary>
        /// Emits a buffer to this room with the specified request type and state.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="type"></param>
        /// <param name="state"></param>
        /// <returns></returns>
        public async UniTask<Relay.EmitResult> Emit(Buffer request, RequestType type, ushort state = Relay.Broadcast) {
            var buffer = new Buffer();
            buffer.Write(InternalId);
            buffer.Write(request);
            return await Connection.Emit(buffer, type);
        }

        /// <summary>
        /// Sends a request to this room and awaits a response of the specified type.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="out"></param>
        /// <param name="in"></param>
        /// <param name="state"></param>
        /// <param name="timeout"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public async UniTask<T> Request<T>(
            RoomRequest request,
            RequestType @out,
            ResponseType @in,
            ushort state = Relay.Broadcast,
            ushort timeout = Relay.DefaultTimeout)
            where T : RoomResponse, new() {
            request.Room = this;
            return await Connection.Request<T>(
                request,
                @out,
                @in,
                state,
                timeout,
                Emit,
                Validate
            );
        }

        private bool Validate<T>(Relay.ValidateInput<T> input) where T : RoomResponse, new() {
            input.payload.Start();
            var instanceId = input.payload.ReadByte();
            if (instanceId != InternalId)
                return false;
            input.response.Room = this;
            return input.response.FromBuffer(input.payload.Clone(1));
        }

        public override string ToString()
            => $"{GetType().Name}[Iid={InternalId}, NodeId={NodeId}, Players={PlayerCount}/{MaxPlayerCount}, Flags={Flags}]";

        public async UniTask<TravelingEvent> Traveling(TravelingRequest request)
            => (await Request<TravelingEvent>(
                   request,
                   RequestType.Traveling,
                   ResponseType.Traveling,
                   Connection.NextState()
               ))
               ?? TravelingEvent.Unknown(this, "Unknown traveling request");
    }
}