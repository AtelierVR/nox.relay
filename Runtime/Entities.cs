using System.Collections.Generic;
using System.Linq;
using Nox.CCK.Utils;
using Nox.Entities;
using Nox.Relay.Runtime.Players;

namespace Nox.Relay.Runtime {
	public class Entities : IEntities {
		public const int InvalidEntityId = -1;

		internal Entities(Session context)
			=> Context = context;

		internal readonly Session Context;

		private int _masterId = InvalidEntityId;

		public int MasterId {
			get => _masterId;
			set {
				if (_masterId == value) return;
				var pm = MasterPlayer;
				_masterId = value;
				var nm = MasterPlayer;
				Context.OnAuthorityTransferredHandler(nm, pm);
			}
		}

		public int LocalId { get; set; } = InvalidEntityId;

		public LocalPlayer LocalPlayer
			=> GetEntity<LocalPlayer>(LocalId);

		public Player MasterPlayer
			=> GetEntity<Player>(MasterId);

		private readonly Dictionary<int, Entity> _entities = new();


		internal void RegisterEntity(Entity entity) {
			if (!_entities.TryAdd(entity.Id, entity)) {
				Logger.LogWarning($"Entity with ID {entity.Id} is already registered.", tag: Context.Tag);
				return;
			}

			Context.OnEntityRegisteredHandler(entity);
		}

		internal void UnregisterEntity(Entity entity) {
			if (!_entities.Remove(entity.Id)) {
				Logger.LogWarning($"Entity with ID {entity.Id} is already not registered.", tag: Context.Tag);
				return;
			}

			Context.OnEntityUnregisteredHandler(entity);
		}

		public IEntity GetEntity(int id)
			=> _entities.GetValueOrDefault(id);

		public T GetEntity<T>(int id) where T : IEntity
			=> GetEntity(id) is T e ? e : default;

		public IEntity[] GetEntities()
			=> _entities.Values.ToArray<IEntity>();

		public T[] GetEntities<T>() where T : IEntity
			=> _entities.Values.OfType<T>().ToArray();

		public bool HasEntity(int id)
			=> _entities.ContainsKey(id);

		public bool HasEntity<T>(int id) where T : IEntity
			=> GetEntity(id) is T;

		public int GetCount()
			=> _entities.Count;

		public int GetCount<T>() where T : IEntity
			=> _entities.Values.OfType<T>().Count();

		// ReSharper disable Unity.PerformanceAnalysis
		public void Dispose() {
			foreach (var entity in _entities.Values.ToArray())
				entity.Dispose();
			_entities.Clear();
		}
	}
}