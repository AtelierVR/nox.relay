using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Nox.CCK.Utils;
using Nox.Entities;
using Nox.Relay.Core.Types.Entities;
using Nox.Sessions;

namespace Nox.Relay.Runtime {
	/// <summary>
	/// Generic networked-entity management of a relay session: create / update / remove / sync an
	/// entity of a registered kind (a prop, a mirror, ...). Entities are standard session entities.
	/// <para>
	/// The session never builds an entity itself: on a spawn received from the relay it emits
	/// <see cref="IEntitySpawn.EventName"/> and the first subscriber answering with an entity wins
	/// (the kinds the relay can build itself answer from <c>Main</c>). A kind then only registers an
	/// applier that copies the relay state into the entity on the following events.
	/// </para>
	/// </summary>
	public sealed partial class Session {
		private readonly Dictionary<string, Action<Entity, EntityEvent>> _a0 = new(StringComparer.OrdinalIgnoreCase);
		private readonly Queue<UniTaskCompletionSource<Entity>> _p0 = new();

		/// <summary>Register (or replace) the applier copying the relay state of a kind into its entity.</summary>
		public void RegisterEntityApplier(string kind, Action<Entity, EntityEvent> applier) {
			if (string.IsNullOrEmpty(kind) || applier == null)
				return;

			_a0[kind] = applier;
		}

		/// <summary>Unregister the applier of an entity kind.</summary>
		public void UnregisterEntityApplier(string kind)
			=> _a0.Remove(kind);

		/// <summary>
		/// Create a networked entity. The relay assigns the id and broadcasts the canonical entity
		/// back; the task completes once it has been created. The transform (initial position included)
		/// and the payload are sent afterwards through their own channels.
		/// </summary>
		public async UniTask<Entity> CreateEntity(string kind, byte flags = 0) {
			if (Room == null) {
				Logger.LogWarning($"Cannot create a '{kind}' entity outside a connected room.", tag: Tag);
				return null;
			}

			var completion = new UniTaskCompletionSource<Entity>();
			_p0.Enqueue(completion);

			var request = new EntityRequest {
				Op    = EntityType.Create,
				Kind  = kind ?? string.Empty,
				Flags = flags,
			};

			if (!await Room.Entity(request))
				// Leave the completion in the queue: a later resolve skips it.
				completion.TrySetResult(null);

			return await completion.Task;
		}

		/// <summary>Remove a networked entity. A relay-owned entity is removed through the relay.</summary>
		public UniTask<bool> RemoveEntity(Entity entity) {
			if (entity == null)
				return UniTask.FromResult(false);

			if (Room != null && entity.IsNetworked)
				return Room.Entity(new EntityRequest { Op = EntityType.Remove, Id = entity.Id });

			entity.Dispose();
			return UniTask.FromResult(true);
		}

		/// <summary>Ask the relay for the full entity list of the room.</summary>
		public UniTask<bool> SyncEntities()
			=> Room != null ? Room.Entity(new EntityRequest { Op = EntityType.Sync }) : UniTask.FromResult(false);

		/// <summary>Apply an entity event received from the relay.</summary>
		internal void OnEntityHandler(EntityEvent @event) {
			if (@event == null)
				return;

			var existing = _entities.GetEntity<Entity>(@event.Id);

			switch (@event.Type) {
				case EntityType.Remove:
					existing?.Dispose();
					DropPendingEntityTransforms(@event.Id);
					break;

				case EntityType.Update:
					ApplyEntity(existing, @event, false);
					break;

				case EntityType.Create:
				case EntityType.Sync:
					var entity = ApplyEntity(existing, @event, true);
					if (@event.Type == EntityType.Create && existing == null && entity != null)
						ResolvePendingCreation(entity);
					if (existing == null && entity != null)
						FlushPendingEntityTransforms(entity.Id);
					break;
			}

			Main.CoreAPI?.EventAPI?.Emit("session_entity_changed", this, @event.Type, @event.Id, @event.Kind);
		}

		private Entity ApplyEntity(Entity existing, EntityEvent @event, bool createIfMissing) {
			var entity = existing;
			if (entity == null) {
				if (!createIfMissing)
					return null;

				// No entity for that id yet: the mods decide what a spawn of that kind is.
				entity = RequestEntitySpawn(@event);
				if (entity == null) {
					Logger.LogWarning($"Nobody answered '{IEntitySpawn.EventName}' for kind '{@event.Kind}' (id {@event.Id})", tag: Tag);
					return null;
				}
			}

			if (_a0.TryGetValue(@event.Kind ?? string.Empty, out var apply) && apply != null)
				apply(entity, @event);

			return entity;
		}

		/// <summary>
		/// Ask the mods which entity to build for a spawn received from the relay. The first answer
		/// wins: as soon as one entity is returned, every following answer is ignored.
		/// </summary>
		private Entity RequestEntitySpawn(EntityEvent @event) {
			Entity entity = null;

			var spawn = new EntitySpawn(this, @event, answer => {
				if (entity != null || answer == null)
					return false;

				// A relay entity is used as-is; any other IEntity is wrapped so the session can
				// register it as a session entity.
				entity = answer is not Entity a 
					? new EntityReference(_entities, answer)
					: a;
				return true;
			});

			Main.CoreAPI.EventAPI.Emit(IEntitySpawn.EventName, this, spawn);

			return entity;
		}

		private void ResolvePendingCreation(Entity entity) {
			while (_p0.Count > 0) {
				var completion = _p0.Dequeue();
				if (completion.TrySetResult(entity))
					return;
			}
		}

		/// <summary><see cref="IEntitySpawn"/> built from a relay <see cref="EntityEvent"/>.</summary>
		private sealed class EntitySpawn : IEntitySpawn {
			private readonly ISession _session;
			private readonly EntityEvent _event;
			private readonly Func<IEntity, bool> _answer;
			private bool _answered;

			public EntitySpawn(ISession session, EntityEvent @event, Func<IEntity, bool> answer) {
				_session = session;
				_event  = @event;
				_answer = answer;
			}

			public ISession Session  => _session;
			public uint   Id       => _event.Id;
			public string Kind     => _event.Kind;
			public uint   OwnerId  => _event.OwnerId;
			public uint   SenderId => _event.SenderId;
			public byte   Flags    => _event.Flags;
			public bool   Answered => _answered;

			public void Callback(IEntity entity) {
				if (entity == null || _answered)
					return;

				if (_answer(entity))
					_answered = true;
			}
		}
	}
}
