using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using Nox.CCK;
using Nox.CCK.Network;
using Nox.CCK.Sessions;
using Nox.CCK.Utils;
using Nox.Entities;
using Nox.Players;
using Nox.Sessions;
using UnityEngine.Events;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Relay.Runtime {
	public partial class Entity : IMultiPartEntity<IPart>, IPhysicalEntity, ISessionEntity, IDisposable {
		readonly internal Entities Context;
		protected Physicals.Physical Physical;
		private PhysicalState _state;

		readonly internal Dictionary<int, IProperty> PropertyMap = new();

		protected Entity(Entities context, uint id) {
			Id = id;
			Context = context;
			context.RegisterEntity(this);
		}

		public uint Id { get; set; }

		/// <summary>
		/// Entity id of the player owning this entity (its creator, then whoever the relay granted it to).
		/// <see cref="IOwnedEntity.MASTER_OWNER_ID"/> (the default) means it belongs to the session master.
		/// </summary>
		public uint OwnerId { get; internal set; } = IOwnedEntity.MASTER_OWNER_ID;

		/// <summary>Apply an ownership decided by the session (the entity may propagate it further).</summary>
		public virtual void ApplyOwner(IPlayer receiver, IPlayer requester)
			=> OwnerId = receiver?.Id
				?? IOwnedEntity.MASTER_OWNER_ID;

		/// <summary>
		/// The player owning this entity: the session master while it has no explicit owner, and the
		/// master again when the player it was granted to is gone.
		/// </summary>
		public IPlayer Owner
			=> OwnerId == IOwnedEntity.MASTER_OWNER_ID
				? Context?.Context?.MasterPlayer
				: Context?.GetEntity<IPlayer>(OwnerId)
					?? Context?.Context?.MasterPlayer;

		/// <summary>Invoked when the ownership of this entity changes (new owner, previous owner).</summary>
		public virtual UnityEvent<IPlayer, IPlayer> OnOwnershipChanged { get; } = new();

		/// <summary>The local client owns this entity: the session streams the pose of its parts.</summary>
		internal bool IsLocalOwner
			=> IsNetworked && Owner is { IsLocal: true };

		public virtual IProperty[] Properties
			=> PropertyMap.Values.ToArray();

		public virtual bool TryGetProperty(int key, out IProperty property) {
			if (PropertyMap.TryGetValue(key, out var prop)) {
				property = prop;
				return true;
			}

			property = null;
			return false;
		}

		public virtual bool SetProperty(IProperty property) {
			if (PropertyMap.TryGetValue(property.Key, out var previous) && previous != null && !ReferenceEquals(previous, property))
				previous.OnUpdated.RemoveListener(NotifyProperty);

			PropertyMap[property.Key] = property;
			property.OnUpdated.AddListener(NotifyProperty);
			return true;
		}

		private UnityAction<IProperty> _notifyProperty;

		/// <summary>Raised on <see cref="OnProperty"/> whenever one of the properties of the entity is applied.</summary>
		private UnityAction<IProperty> NotifyProperty
			=> _notifyProperty ??= OnProperty.Invoke;

		/// <summary>
		/// Declare a string property of this entity (no-op when it already exists). Its value travels
		/// as UTF-8 bytes, like any other property value.
		/// </summary>
		public virtual bool DefineProperty(string name, object value = null, PropertyFlags flags = PropertyFlags.Synced) {
			if (string.IsNullOrEmpty(name) || PropertyMap.ContainsKey(name.Hash()))
				return false;

			SetProperty(new Property(this, name, value, flags));
			return true;
		}

		/// <summary>Read a string property, or <c>null</c> when it was never set.</summary>
		public string GetPropertyString(string name) {
			if (string.IsNullOrEmpty(name) || !TryGetProperty(name.Hash(), out var property) || property == null)
				return null;

			return property.Value switch {
				string str => str,
				byte[] { Length: > 0 } raw => Encoding.UTF8.GetString(raw),
				_ => null,
			};
		}

		/// <summary>Set a string property and mark it dirty so it is synced on the next tick.</summary>
		public void SetPropertyString(string name, string value) {
			if (string.IsNullOrEmpty(name))
				return;

			if (!TryGetProperty(name.Hash(), out var property) || property == null) {
				property = new Property(this, name, value, PropertyFlags.Synced);
				SetProperty(property);
			} else {
				property.Value = value;
			}

			property.IsDirty = true;
		}

		#region Data

		// Not stored in PropertyMap on purpose: data must never be picked up by the avatar parameter
		// synchronization, so an avatar cannot read or write it (anti-cheat).
		private readonly DataContainer _data = new();

		public IDataContainer Data
			=> _data;

		#endregion

		#region Parts

		/// <summary>
		/// Parts of the entity: a rig part of a player, a sub-element of a prop (preview mesh, label,
		/// icon, ...). The entity only stores them: what a part is and how it is created belongs to the
		/// concrete entity (<see cref="CreatePart"/>).
		/// </summary>
		readonly internal Dictionary<ushort, IPart> Parts = new();

		public IPart[] GetParts()
			=> Parts.Values.ToArray();

		public bool TryGetPart(ushort id, out IPart part)
			=> Parts.TryGetValue(id, out part);

		/// <summary>Create the part of the given rig index, or <c>null</c> when the entity has none.</summary>
		protected virtual IPart CreatePart(ushort index)
			=> null;

		/// <summary>Get the part of the given rig index, creating it if needed.</summary>
		public IPart GetOrCreatePart(ushort index) {
			if (!Parts.TryGetValue(index, out var part)) {
				part = CreatePart(index);
				if (part == null)
					return null;
				Parts[index] = part;
			}

			return part;
		}

		/// <summary>A pose received for a part unknown yet creates it: the sender only names the parts it drives.</summary>
		public void ApplyPart(ushort id, TransformObject transform, uint time = 0)
			=> GetOrCreatePart(id)?.Apply(transform, time);

		/// <summary>
		/// Whether a transform sent by <paramref name="senderId"/> must be ignored because the parts
		/// of this entity are authoritative locally (the local player drives its own rig).
		/// </summary>
		public virtual bool IgnoresTransformEcho(uint senderId)
			=> senderId != 0 && senderId == Context?.LocalId;

		/// <summary>Called once a received transform has been copied onto one of the parts.</summary>
		/// <param name="partId">Part that was updated.</param>
		/// <param name="time">Clock of the sender when the transform was sampled, in milliseconds (0 = not set).</param>
		protected internal virtual void OnPartTransformReceived(ushort partId, uint time) { }

		public virtual UnityEvent<IProperty> OnProperty { get; } = new();

		/// <summary>Raise <see cref="OnEvent"/> (a session event was routed to this entity).</summary>
		internal void RaiseEvent(long name, byte[] payload, uint senderId)
			=> OnEvent.Invoke(name, payload, senderId);

		public virtual UnityEvent<long, byte[], uint> OnEvent { get; } = new();

		ISession ISessionEntity.Session
			=> Context?.Context;

		void ISessionEntity.Setup(ISession session) { }

		bool IOwnedEntity.IsOwner
			=> IsLocalOwner;

		void ISessionEntity.EmitEvent(long name, byte[] payload)
			=> Context?.Context?.EmitEvent(name, payload).Forget();

		#endregion

		virtual protected UniTask<Physicals.Physical> InstantiatePhysical() {
			Logger.LogWarning($"Entity {Id} does not implement {nameof(InstantiatePhysical)}, cannot create physical representation.", tag: nameof(Entity));
			return UniTask.FromResult<Physicals.Physical>(null);
		}

		/// <summary>
		/// Whether this entity is replicated by the relay (its id and lifecycle are relay-owned).
		/// A networked prop returns true; local-only entities return false.
		/// </summary>
		public virtual bool IsNetworked
			=> false;

		virtual protected bool IsVisible
			=> false;

		public virtual void Update() {
			if (IsVisible) {
				if (!Physical)
					MakePhysical().Forget();
				else if (Physical.IsDestroying)
					// Physical exists but is hidden (delayed destroy pending): cancel and re-enable
					Physical.CancelDestroy();
			} else {
				if (Physical && !Physical.IsDestroying)
					DestroyPhysical();
			}
		}

		public PhysicalState PhysicalState
			=> _state;

		public bool TryGetPhysical<T>(out T physical) where T : Physical {
			if (Physical is T p) {
				physical = p;
				return true;
			}

			physical = null;
			return false;
		}

		public async UniTask<bool> MakePhysical() {
			if (Physical)
				return true;

			if (_state != PhysicalState.None)
				return false;

			_state = PhysicalState.Creating;
			try {
				Physical = await InstantiatePhysical();
			} finally {
				_state = PhysicalState.None;
			}

			if (!Physical)
				return false;

			Physical.ActuallyDestroyed.AddListener(HandlePhysicalActuallyDestroyed);
			OnPhysicalCreated();
			return true;
		}

		private void HandlePhysicalActuallyDestroyed() {
			if (!Physical)
				return;

			Physical = null;
			_state = PhysicalState.None;
			OnPhysicalDestroyed();
		}

		/// <summary>
		/// Called after the physical representation is successfully created.
		/// Override this in derived classes to perform additional initialization.
		/// </summary>
		virtual protected void OnPhysicalCreated() {
			// Base implementation does nothing
		}

		/// <summary>
		/// Called after the physical representation is destroyed.
		/// Override this in derived classes to perform additional cleanup.
		/// </summary>
		virtual protected void OnPhysicalDestroyed() {
			// Base implementation does nothing
		}

		UniTask IPhysicalEntity.DestroyPhysical() {
			DestroyPhysical(true);
			return UniTask.CompletedTask;
		}

		public void DestroyPhysical(bool immediate = false) {
			if (!Physical)
				return;

			var physical = Physical;
			_state = PhysicalState.Destroying;

			if (immediate) {
				Physical = null;
				_state = PhysicalState.None;
				physical.Destroy(true);
				OnPhysicalDestroyed();
				return;
			}
			physical.Destroy();
		}

		public virtual void Dispose() {
			DestroyPhysical(true);
			Parts.Clear();
			_data.Clear();
			Context.UnregisterEntity(this);
		}

		public override string ToString()
			=> $"{GetType().Name}[Id={Id}]";
	}
}
