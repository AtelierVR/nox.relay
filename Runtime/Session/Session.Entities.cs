using Nox.Entities;
using UnityEngine.Events;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Relay.Runtime {
	/// <summary>Entity registration/unregistration notifications.</summary>
	public sealed partial class Session {
		public UnityEvent<IEntity> OnEntityRegistered { get; } = new();
		public UnityEvent<IEntity> OnEntityUnregistered { get; } = new();

		public void OnEntityRegisteredHandler(IEntity entity) {
			Logger.LogDebug($"OnEntityRegistered: {entity}", tag: Tag);

			Main.CoreAPI.EventAPI.Emit("session_entity_registered", this, entity);
			OnEntityRegistered.Invoke(entity);

			foreach (var module in GetAllModules())
				module.OnEntityRegistered(entity);
		}

		public void OnEntityUnregisteredHandler(IEntity entity) {
			Logger.LogDebug($"OnEntityUnregistered: {entity}", tag: Tag);

			Main.CoreAPI.EventAPI.Emit("session_entity_unregistered", this, entity);
			OnEntityUnregistered.Invoke(entity);

			foreach (var module in GetAllModules())
				module.OnEntityUnregistered(entity);
		}
	}
}
