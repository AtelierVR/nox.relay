using System;
using System.Collections.Generic;
using System.Linq;
using Nox.CCK.Utils;
using Nox.Entities;
using Nox.Relay.Runtime.Players;
using UnityEngine.Events;

namespace Nox.Relay.Runtime {
	public class Entities : IEntities {
		/// <summary>Id used when an entity has none (local-only before creation, no master, ...).</summary>
		public const uint InvalidEntityId = uint.MaxValue;

		internal Entities(Session context)
			=> Context = context;

		readonly internal Session Context;

		private uint _masterId = InvalidEntityId;

		public uint MasterId {
			get => _masterId;
			set {
				if (_masterId == value)
					return;
				var pm = MasterPlayer;
				_masterId = value;
				var nm = MasterPlayer;
				Context.OnAuthorityTransferredHandler(nm, pm);
			}
		}

		public uint LocalId { get; set; } = InvalidEntityId;

		public LocalPlayer LocalPlayer
			=> GetEntity<LocalPlayer>(LocalId);

		public Player MasterPlayer
			=> GetEntity<Player>(MasterId);

		private readonly Dictionary<uint, IEntity> _entities = new();

		internal void RegisterEntity(IEntity entity) {
			if (_entities.TryGetValue(entity.Id, out var known)) {
				if (ReferenceEquals(known, entity))
					return;

				Logger.LogWarning($"Entity with ID {entity.Id} is already registered.", tag: Context.Tag);
				return;
			}

			_entities.Add(entity.Id, entity);

			Context.OnEntityRegisteredHandler(entity);
			OnEntityAdded.Invoke(entity);
		}

		internal void UnregisterEntity(IEntity entity) {
			if (!_entities.Remove(entity.Id)) {
				Logger.LogWarning($"Entity with ID {entity.Id} is already not registered.", tag: Context.Tag);
				return;
			}

			Context.OnEntityUnregisteredHandler(entity);
			OnEntityRemoved.Invoke(entity);
		}

		public IEntity GetEntity(uint id)
			=> _entities.GetValueOrDefault(id);

		public T GetEntity<T>(uint id) where T : IEntity
			=> GetEntity(id) is T e ? e : default;

		public IEntity[] GetEntities()
			=> _entities.Values.ToArray<IEntity>();

		public T[] GetEntities<T>() where T : IEntity
			=> _entities.Values.OfType<T>().ToArray();

		public bool HasEntity(uint id)
			=> _entities.ContainsKey(id);

		public bool HasEntity<T>(uint id) where T : IEntity
			=> GetEntity(id) is T;

		public int GetCount()
			=> _entities.Count;

		public int GetCount<T>() where T : IEntity
			=> _entities.Values.OfType<T>().Count();

		public UnityEvent<IEntity> OnEntityAdded { get; } = new();
		public UnityEvent<IEntity> OnEntityRemoved { get; } = new();

		// ReSharper disable Unity.PerformanceAnalysis
		public void Dispose() {
			foreach (var entity in _entities.Values.ToArray())
				if (entity is IDisposable disposable)
					disposable.Dispose();

			_entities.Clear();
		}
	}
}
