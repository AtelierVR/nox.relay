using Cysharp.Threading.Tasks;
using Nox.CCK.Events;
using Nox.CCK.Network;
using Nox.Players;
using Nox.Relay.Core.Types.Event;
using UnityEngine.Events;
using Logger = Nox.CCK.Utils.Logger;
using Player = Nox.Relay.Runtime.Players.Player;

namespace Nox.Relay.Runtime {
	/// <summary>Custom session events (broadcast/receive).</summary>
	public sealed partial class Session {
		public UnityEvent<long, byte[], IPlayer> OnEventReceived { get; } = new();

		/// <summary>
		/// Raised before an incoming event is forwarded to <see cref="OnEventReceived"/> and the
		/// modules. A handler can set <see cref="EventDispatchContext.Handled"/> to consume a private
		/// event (a per-listener mute, for instance) without exposing it to every mod.
		/// </summary>
		public readonly NoxEvent<EventDispatchContext> OnEventIntercept = new();

		public int EventPayloadSize
			=> EventRequest.MaxPayloadSize;

		public UniTask<bool> EmitEvent(long @event, byte[] raw)
			=> Room.Event(EventRequest.Broadcast(@event, raw));

		/// <summary>
		/// Sends an event only to the given players. Used for private relations such as a
		/// per-listener mute, which must stay between the two players involved.
		/// </summary>
		public UniTask<bool> EmitEventTargeted(long @event, byte[] raw, params uint[] targetIds)
			=> Room.Event(EventRequest.CreateTargeted(@event, raw, targetIds));

		/// <summary>Sends an event by name, hashed the same way as <see cref="EmitEvent"/>.</summary>
		public UniTask<bool> EmitEventTargeted(string @event, byte[] raw, params uint[] targetIds)
			=> EmitEventTargeted(@event.Hash(), raw, targetIds);

		internal void OnEventHandler(EventEvent @event) {
			var player = _entities.GetEntity<Player>(@event.SenderId);
			if (player == null) {
				Logger.LogWarning($"Player with ID {@event.SenderId} not found for Event event", tag: Tag);
				return;
			}

			var context = new EventDispatchContext(@event.Name, @event.Payload, player);
			OnEventIntercept.Invoke(context);
			if (context.Handled)
				return;

			Main.CoreAPI.EventAPI.Emit("session_event_triggered", this, @event.Name, @event.Payload, player);
			OnEventReceived.Invoke(@event.Name, @event.Payload, player);

			foreach (var module in GetModules())
				module.OnEvent(@event.Name, @event.Payload, player);
		}
	}

	/// <summary>Mutable context of an incoming session event, used by <see cref="Session.OnEventIntercept"/>.</summary>
	public sealed class EventDispatchContext {
		public EventDispatchContext(long name, byte[] payload, IPlayer sender) {
			Name    = name;
			Payload = payload;
			Sender  = sender;
		}

		public long   Name    { get; }
		public byte[] Payload { get; }
		public IPlayer Sender { get; }

		/// <summary>Set to true to stop the event from reaching the public events and the modules.</summary>
		public bool Handled { get; set; }
	}
}
