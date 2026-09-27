using Cysharp.Threading.Tasks;
using Nox.Players;
using Nox.Relay.Core.Types.Event;
using UnityEngine.Events;
using Logger = Nox.CCK.Utils.Logger;
using Player = Nox.Relay.Runtime.Players.Player;

namespace Nox.Relay.Runtime {
	/// <summary>Custom session events (broadcast/receive).</summary>
	public sealed partial class Session {
		public UnityEvent<long, byte[], IPlayer> OnEventReceived { get; } = new();

		public int EventPayloadSize
			=> EventRequest.MaxPayloadSize;

		public UniTask<bool> EmitEvent(long @event, byte[] raw)
			=> Room.Event(EventRequest.Broadcast(@event, raw));

		internal void OnEventHandler(EventEvent @event) {
			var player = InterEntities.GetEntity<Player>(@event.SenderId);
			if (player == null) {
				Logger.LogWarning($"Player with ID {@event.SenderId} not found for Event event", tag: Tag);
				return;
			}

			Main.CoreAPI.EventAPI.Emit("session_event_triggered", this, @event.Name, @event.Payload, player);
			OnEventReceived.Invoke(@event.Name, @event.Payload, player);

			foreach (var module in GetAllModules())
				module.OnEvent(@event.Name, @event.Payload, player);
		}
	}
}
