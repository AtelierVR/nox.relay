using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Nox.CCK.Utils;
using Nox.Entities;
using Nox.Players;
using Nox.Relay.Core.Types.Entities;
using Nox.Sessions;
using UnityEngine.Events;

namespace Nox.Relay.Runtime {
	/// <summary>
	/// Generic networked-entity management of a relay session: create / remove / sync an entity of a
	/// given type (a prop, a mirror, ...), and request its ownership. Entities are standard session
	/// entities.
	/// <para>
	/// The session never builds an entity itself: on a spawn received from the relay it emits
	/// <see cref="IEntitySpawn.EventName"/> and the first subscriber answering with an entity wins
	/// (the types the relay can build itself answer from <c>Main</c>). A type then only registers an
	/// applier that copies the relay state into the entity on the following events.
	/// </para>
	/// </summary>
	public sealed partial class Session : ISessionSpawner, ISessionOwnership {
		private readonly Dictionary<uint, Action<IEntity, EntityEvent>> _a0 = new();
		private readonly Queue<UniTaskCompletionSource<IEntity>> _p0 = new();

		/// <summary>Invoked when the ownership of an entity changed (entity, owner, previous owner).</summary>
		public UnityEvent<IEntity, IPlayer, IPlayer> OnOwnershipChanged { get; } = new();

		/// <summary>Invoked when a player asks for the ownership of an entity (entity, requester, request id).</summary>
		public UnityEvent<IEntity, IPlayer, ushort> OnOwnershipRequested { get; } = new();

		/// <summary>Register (or replace) the applier copying the relay state of a type into its entity.</summary>
		public void RegisterEntityApplier(uint type, Action<IEntity, EntityEvent> applier) {
			if (type == 0 || applier == null)
				return;

			_a0[type] = applier;
		}

		/// <summary>Unregister the applier of an entity type.</summary>
		public void UnregisterEntityApplier(uint type)
			=> _a0.Remove(type);

		/// <summary>
		/// Create a networked entity of the given type. The relay assigns the id and broadcasts the
		/// canonical entity back; the task completes once it has been created. The transform (initial
		/// position included) and the payload are sent afterwards through their own channels.
		/// <para>
		/// An entity is identified by its type: creating one the room already holds (a rejoin, a master change,
		/// a world reloaded) answers the id it was already assigned.
		/// </para>
		/// </summary>
		public async UniTask<IEntity> CreateEntity(uint type, byte flags = 0) {
			if (Room == null) {
				Logger.LogWarning($"Cannot create a type {type} entity outside a connected room.", tag: Tag);
				return null;
			}

			var completion = new UniTaskCompletionSource<IEntity>();
			_p0.Enqueue(completion);

			var request = new EntityRequest {
				Op = EntityType.Create,
				Type = type,
				Flags = flags,
			};

			if (!await Room.Entity(request))
				// Leave the completion in the queue: a later resolve skips it.
				completion.TrySetResult(null);

			return await completion.Task;
		}

		/// <summary>Remove an entity: a replicated one goes through the relay, so every client removes it too.</summary>
		public UniTask<bool> RemoveEntity(IEntity entity) {
			if (entity == null)
				return UniTask.FromResult(false);

			if (Room != null && entity is ISessionEntity { IsNetworked: true })
				return Room.Entity(new EntityRequest {
					Op = EntityType.Remove,
					Id = entity.Id
				});

			entity.Dispose();
			return UniTask.FromResult(true);
		}

		/// <summary>Ask the relay for the full entity list of the room.</summary>
		public UniTask<bool> SyncEntities()
			=> Room != null ? Room.Entity(new EntityRequest { Op = EntityType.Sync }) : UniTask.FromResult(false);

		/// <summary>
		/// Ask the relay for the ownership of an entity and don't wait for its answer. The relay changes
		/// it straight away for a privileged player; otherwise the request is forwarded to them and the
		/// outcome arrives as <see cref="EntityOwnership.Changed"/> (granted) or
		/// <see cref="EntityType.Error"/> (refused).
		/// </summary>
		public UniTask<OwnershipChangeResult> RequestOwnership(uint id)
			=> ChangeOwnership(id);

		/// <summary>
		/// Change the ownership of an entity. Only a privileged player may name another
		/// <paramref name="playerId"/>; <c>0</c> is the sender itself. A privileged player granting a
		/// forwarded request (see <see cref="OWNERSHIP_REQUESTED_EVENT"/>) names its requester here.
		/// </summary>
		/// <param name="entityId">Entity whose ownership is wanted.</param>
		/// <param name="playerId">Player that would get it (<c>0</c> = the sender itself).</param>
		/// <param name="wait">
		/// Wait for the answer: <c>false</c> sends the request and returns
		/// <see cref="OwnershipChangeResult.Pending"/>; <c>true</c> completes once the ownership was
		/// granted or refused.
		/// </param>
		/// <param name="token">Cancels the wait (also cancelled when the server disconnects).</param>
		public async UniTask<OwnershipChangeResult> ChangeOwnership(
			uint entityId,
			uint playerId = 0,
			bool wait = false,
			CancellationToken token = default
		) {
			if (Room == null)
				return OwnershipChangeResult.Failed;

			if (!wait)
				return await SendOwnershipChange(entityId, playerId)
					? OwnershipChangeResult.Pending
					: OwnershipChangeResult.Failed;

			var completion = new UniTaskCompletionSource<bool>();

			// Only one request per entity: a new one supersedes the previous one.
			if (_ownershipWaits.Remove(entityId, out var previous))
				previous.TrySetResult(false);

			_ownershipWaits[entityId] = completion;

			using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, ConnectionToken);

			try {
				if (!await SendOwnershipChange(entityId, playerId))
					return OwnershipChangeResult.Failed;

				// The relay answers within the client timeout it granted to the connection.
				var granted = await completion.Task
					.AttachExternalCancellation(linked.Token)
					.Timeout(ConnectionTimeout);

				return granted ? OwnershipChangeResult.Granted : OwnershipChangeResult.Refused;
			} catch (TimeoutException) {
				Logger.LogWarning($"No answer for the ownership of entity {entityId}", tag: Tag);
				return OwnershipChangeResult.Timeout;
			} catch (OperationCanceledException) {
				Logger.LogDebug($"Ownership of entity {entityId} abandoned", tag: Tag);
				return OwnershipChangeResult.Cancelled;
			} finally {
				_ownershipWaits.Remove(entityId);
			}
		}

		/// <summary>Send the ownership request of an entity through the relay entity channel.</summary>
		internal UniTask<bool> SendOwnershipChange(uint entityId, uint playerId)
			=> Room != null
				? Room.Entity(new EntityRequest {
					Op = EntityType.Ownership,
					Sub = EntityOwnership.Change,
					Id = entityId,
					PlayerId = playerId,
				})
				: UniTask.FromResult(false);

		/// <summary>Ownership changes waiting for their answer, by entity id.</summary>
		private readonly Dictionary<uint, UniTaskCompletionSource<bool>> _ownershipWaits = new();

		/// <summary>Answer the waiting ownership change of an entity, if any.</summary>
		private void ResolveOwnershipWait(uint id, bool granted) {
			if (_ownershipWaits.Remove(id, out var completion))
				completion.TrySetResult(granted);
		}

		/// <summary>Refuse a forwarded ownership request; the requester then receives an error.</summary>
		public UniTask<bool> RefuseOwnership(ushort request)
			=> Room != null
				? Room.Entity(new EntityRequest {
					Op = EntityType.Ownership,
					Sub = EntityOwnership.Refuse,
					Request = request,
				})
				: UniTask.FromResult(false);

		/// <summary>
		/// Name of the event carrying a forwarded ownership request (data: the session, the entity id,
		/// the requester, the request id, then an <see cref="Action{T}"/> of <see cref="object"/> array
		/// the scripts answer with). Only the master answers it.
		/// </summary>
		public const string OWNERSHIP_REQUESTED_EVENT = "session_entity_ownership_requested";

		/// <summary>
		/// Handle a forwarded ownership request. Only the master answers it: the scripts of the world
		/// decide through <see cref="OWNERSHIP_REQUESTED_EVENT"/> (refusing means answering its
		/// callback with <c>false</c>), and when no script refuses the ownership is granted.
		/// </summary>
		private void HandleOwnershipRequested(EntityEvent @event) {
			if (MasterPlayer is not { IsLocal: true })
				return;

			Logger.LogDebug(
				$"Ownership of entity {@event.Id} requested by {@event.SenderId} (request {@event.Request})",
				tag: Tag
			);

			var accepted = true;

			// The session modules (the world scripts) observe and decide: one refusal is enough.
			var entity = _entities.GetEntity(@event.Id);
			var requester = _entities.GetEntity<IPlayer>(@event.SenderId);

			if (entity != null && requester != null) {
				OnOwnershipRequested.Invoke(entity, requester, @event.Request);

				// The entity itself may refuse (a locked player, a prop that must stay with its owner).
				if (entity is IOwnedEntity owned
					&& owned.TryDecideOwnership(requester, requester, out var ownAnswer)
					&& !ownAnswer)
					accepted = false;

				foreach (var module in GetModules()) {
					// A request always asks the ownership for the requester itself.
					if (module is IOwnershipListener listener)
						listener.OnOwnershipRequested(entity, requester, requester);

					if (module is IOwnershipDecision decision
						&& decision.TryDecideOwnership(entity, requester, requester, out var answer)
						&& !answer)
						accepted = false;
				}
			}

			Main.CoreAPI?.EventAPI?.Emit(
				OWNERSHIP_REQUESTED_EVENT,
				this,
				@event.Id,
				@event.SenderId,
				@event.Request,
				new Action<object[]>(args => {
					if (args.Length > 0 && args[0] is false)
						accepted = false;
				})
			);

			if (accepted)
				SendOwnershipChange(@event.Id, @event.SenderId).Forget();
			else
				RefuseOwnership(@event.Request).Forget();
		}

		/// <summary>Apply an entity event received from the relay.</summary>
		internal void OnEntityHandler(EntityEvent @event) {
			if (@event == null)
				return;

			switch (@event.Op) {
				case EntityType.Error:
					Logger.LogWarning(
						$"Entity operation refused by the relay: {@event.Result} (id {@event.Id}, type {@event.Type})",
						tag: Tag
					);

					// A refused create carries no id: release the caller waiting for it.
					if (@event.Id == 0)
						ResolvePendingCreation(null);
					else
						ResolveOwnershipWait(@event.Id, false);

					return;

				case EntityType.Ownership:
					switch (@event.Sub) {
						case EntityOwnership.Changed:
							if (_entities.GetEntity(@event.Id) is { } changed)
								ApplyOwnership(
									changed,
									_entities.GetEntity<IPlayer>(@event.OwnerId),
									_entities.GetEntity<IPlayer>(@event.SenderId)
								);
							break;

						case EntityOwnership.Requested:
							HandleOwnershipRequested(@event);
							break;
					}
					break;

				case EntityType.Remove:
					if (_entities.GetEntity(@event.Id) is { } removed) {
						removed.Dispose();

						if (_entities.HasEntity(@event.Id))
							_entities.UnregisterEntity(removed);
					}

					DropPendingEntityTransforms(@event.Id);
					break;

				case EntityType.Create:
				case EntityType.Sync:
					var existing = _entities.GetEntity(@event.Id);
					var entity = ApplyEntity(existing, @event);

					if (@event.Op == EntityType.Create && existing == null && entity != null)
						ResolvePendingCreation(entity);

					if (existing == null && entity != null)
						FlushPendingEntityTransforms(entity.Id);
					break;
			}

			Main.CoreAPI?.EventAPI?.Emit("session_entity_changed", this, @event.Op, @event.Id, @event.Type);
		}

		/// <summary>
		/// Copy an ownership received from the relay onto the entity of the session.
		/// </summary>
		/// <param name="entity">Entity whose ownership changed.</param>
		/// <param name="receiver">Player the entity is given to (the new owner).</param>
		/// <param name="requester">Player that asked for the ownership, if any.</param>
		private void ApplyOwnership(IEntity entity, IPlayer receiver, IPlayer requester) {
			if (entity is not IOwnedEntity owned)
				return;

			Logger.LogDebug(
				$"Entity {entity.Id} is now owned by {receiver?.Id ?? 0} (requested by {requester?.Id ?? 0})",
				tag: Tag
			);

			var previous = owned.Owner;

			owned.ApplyOwner(receiver, requester);

			NotifyOwnership(entity, receiver, previous, requester);
			ResolveOwnershipWait(entity.Id, receiver?.Id == _entities.LocalId);
		}

		/// <summary>
		/// Announce an ownership of an entity: the scripts, the session modules and everybody listening to
		/// the entity itself.
		/// </summary>
		private void NotifyOwnership(IEntity entity, IPlayer @new, IPlayer old, IPlayer requester) {
			Main.CoreAPI?.EventAPI?.Emit(
				"session_entity_ownership_changed",
				this,
				entity,
				@new,
				old,
				requester
			);

			foreach (var module in GetModules())
				if (module is IOwnershipListener listener)
					listener.OnOwnershipChanged(entity, @new, old);

			if (entity is IOwnedEntity owned)
				owned.OnOwnershipChanged.Invoke(@new, old);

			OnOwnershipChanged.Invoke(entity, @new, old);
		}

		/// <summary>
		/// The authority changed: every entity that follows it belongs to whoever holds it now, so its
		/// owner changed too. Nothing is transferred (the owner is resolved live), the events are raised.
		/// </summary>
		private void RefreshMasterOwnership(IPlayer master, IPlayer previous) {
			foreach (var entity in _entities.GetEntities()) {
				if (entity is not IOwnedEntity owned || !FollowsMaster(entity, owned))
					continue;

				NotifyOwnership(entity, master, previous, null);
			}
		}

		/// <summary>
		/// Whether an entity follows the authority: it has no explicit owner, or the player it was
		/// granted to is gone (the ownership falls back to the master in both cases).
		/// </summary>
		private bool FollowsMaster(IEntity entity, IOwnedEntity owned)
			=> entity is Entity local
				? local.OwnerId == IOwnedEntity.MASTER_OWNER_ID
					|| _entities.GetEntity<IPlayer>(local.OwnerId) == null
				: owned.Owner == null;

		private IEntity ApplyEntity(IEntity existing, EntityEvent @event) {
			var entity = existing;
			if (entity == null) {
				entity = RequestEntitySpawn(@event);
				if (entity == null) {
					Logger.LogWarning($"Nobody answered '{IEntitySpawn.EventName}' for type {@event.Type} (id {@event.Id})", tag: Tag);
					return null;
				}

				_entities.RegisterEntity(entity);
			}

			var owner = _entities.GetEntity<IPlayer>(@event.OwnerId);
			if (owner != null && entity is IOwnedEntity owned)
				owned.ApplyOwner(owner, null);

			if (_a0.TryGetValue(@event.Type, out var apply) && apply != null)
				apply(entity, @event);

			return entity;
		}

		/// <summary>
		/// Ask the mods which entity to build for a spawn received from the relay. The first answer
		/// wins: as soon as one entity is returned, every following answer is ignored.
		/// </summary>
		private IEntity RequestEntitySpawn(EntityEvent @event) {
			IEntity entity = null;

			var spawn = new EntitySpawn(this, @event, answer => {
				if (entity != null || answer == null)
					return false;

				entity = answer;
				return true;
			});

			Main.CoreAPI.EventAPI.Emit(IEntitySpawn.EventName, this, spawn);

			return entity;
		}

		private void ResolvePendingCreation(IEntity entity) {
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
				_event = @event;
				_answer = answer;
			}

			public ISession Session => _session;
			public uint Id => _event.Id;
			public uint Type => _event.Type;
			public uint OwnerId => _event.OwnerId;
			public uint SenderId => _event.SenderId;
			public byte Flags => _event.Flags;
			public bool Answered => _answered;

			public void Callback(IEntity entity) {
				if (entity == null || _answered)
					return;

				if (_answer(entity))
					_answered = true;
			}
		}
	}
}
