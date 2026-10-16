using Nox.Entities;
using UnityEngine.Events;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Relay.Runtime {
	/// <summary>Entity registry of the session and registration/unregistration notifications.</summary>
	public sealed partial class Session {
		/// <summary>Entities of the session (players and networked entities share it).</summary>
		internal Entities _entities;

		/// <summary>Create the entity registry of this session (called once from the constructor).</summary>
		private void SetupEntities()
			=> _entities = new Entities(this);

		public IEntities Entities
			=> _entities;

		public UnityEvent<IEntity> OnEntityRegistered { get; } = new();
		public UnityEvent<IEntity> OnEntityUnregistered { get; } = new();

		public void OnEntityRegisteredHandler(IEntity entity) {
			Logger.LogDebug($"OnEntityRegistered: {entity}", tag: Tag);

			Main.CoreAPI.EventAPI.Emit("session_entity_registered", this, entity);
			OnEntityRegistered.Invoke(entity);

			foreach (var module in GetModules())
				module.OnEntityRegistered(entity);
		}

		public void OnEntityUnregisteredHandler(IEntity entity) {
			Logger.LogDebug($"OnEntityUnregistered: {entity}", tag: Tag);

			Main.CoreAPI.EventAPI.Emit("session_entity_unregistered", this, entity);
			OnEntityUnregistered.Invoke(entity);

			foreach (var module in GetModules())
				module.OnEntityUnregistered(entity);
		}
	}
}
