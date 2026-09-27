using System.Linq;
using Nox.Entities;
using Nox.Relay.Core.Types.Properties;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Relay.Runtime {
	/// <summary>Incoming property synchronization for entities.</summary>
	public sealed partial class Session {
		internal void OnPropertiesHandler(PropertiesEvent @event) {
			var entity = InterEntities.GetEntity<Entity>(@event.EntityId);
			if (entity == null) {
				Logger.LogWarning($"Entity with ID {@event.EntityId} not found for Properties event", tag: Tag);
				return;
			}

			var sender = InterEntities.GetEntity<Entity>(@event.SenderId);
			if (sender == null) {
				Logger.LogWarning($"Entity with ID {@event.SenderId} not found for Properties event", tag: Tag);
				return;
			}

			var table = entity.Properties
				.ToDictionary(p => p.Key, p => p.Value);

			var fromLocal = entity.Id == sender.Id;

			foreach (var param in @event.Parameters) {
				if (!table.TryGetValue(param.Key, out var property)) {
					Logger.LogWarning($"Property with key {param.Key} not found for entity {entity.Id}, creating unassigned property.", tag: Tag);
					property = new UnassignedProperty(entity, param.Key, param.Value);
					entity.SetProperty(property);
					continue;
				}

				if (!property.Flags.HasFlag(fromLocal ? PropertyFlags.RemoteEmit : PropertyFlags.LocalEmit)) {
					Logger.LogWarning($"Ignoring non-synced property: {sender.Id} -> {entity.Id} ({property.Name ?? property.Key.ToString()}) [{fromLocal}, {property.Flags}]", tag: Tag);
					continue;
				}

				property.Deserialize(param.Value);
				property.IsDirty = false;
			}
		}
	}
}
