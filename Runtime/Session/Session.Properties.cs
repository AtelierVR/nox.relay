using System.Linq;
using Nox.Entities;
using Nox.Relay.Core.Types.Properties;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Relay.Runtime {
	/// <summary>Incoming property synchronization for entities.</summary>
	public sealed partial class Session {
		internal void OnPropertiesHandler(PropertiesEvent @event) {
			var entity = _entities.GetEntity(@event.EntityId);
			if (entity is not IPropertiesEntity properties) {
				Logger.LogWarning($"Entity with ID {@event.EntityId} not found for Properties event", tag: Tag);
				return;
			}

			var sender = _entities.GetEntity(@event.SenderId);
			if (sender == null) {
				Logger.LogWarning($"Entity with ID {@event.SenderId} not found for Properties event", tag: Tag);
				return;
			}

			var table = properties.Properties
				.ToDictionary(p => p.Key, p => p);

			var fromLocal = entity.Id == sender.Id;

			foreach (var param in @event.Properties) {
				if (!table.TryGetValue(param.Key, out var property)) {
					Logger.LogWarning($"Property with key {param.Key} not found for entity {entity.Id}, creating unassigned property.", tag: Tag);

					if (!properties.SetProperty(new UnassignedProperty(entity, param.Key, param.Value)))
						Logger.LogWarning($"Cannot create unassigned property {param.Key} for entity {entity.Id}: it does not accept new properties.", tag: Tag);

					continue;
				}

				if (!property.Flags.HasFlag(fromLocal ? PropertyFlags.RemoteEmit : PropertyFlags.LocalEmit)) {
					Logger.LogWarning($"Ignoring non-synced property: {sender.Id} -> {entity.Id} ({property.Name ?? property.Key.ToString()}) [{fromLocal}, {property.Flags}]", tag: Tag);
					continue;
				}

				property.Apply(param.Value);
			}
		}
	}
}
