using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
using Nox.CCK.Network;
using Nox.CCK.Sessions;
using Nox.Entities;
using Nox.Relay.Core.Types.Properties;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Relay.Runtime {
	public class Entity : IEntity, IMultiPartEntity<IPart> {
		readonly internal Entities Context;
		protected Physicals.Physical Physical;

		readonly internal Dictionary<int, IProperty> Properties = new();

		protected Entity(Entities context, uint id) {
			Id      = id;
			Context = context;
			context.RegisterEntity(this);
		}

		public uint Id { get; }

		public IProperty[] GetProperties()
			=> Properties.Values.ToArray();

		public bool TryGetProperty(int key, out IProperty property) {
			if (Properties.TryGetValue(key, out var prop)) {
				property = prop;
				return true;
			}

			property = null;
			return false;
		}

		public void SetProperty(IProperty property)
			=> Properties[property.Key] = property;

		/// <summary>
		/// Declare a string property of this entity (no-op when it already exists). Its value travels
		/// as UTF-8 bytes, like any other property value.
		/// </summary>
		public bool DefineProperty(string name, string value = null, PropertyFlags flags = PropertyFlags.Synced) {
			if (string.IsNullOrEmpty(name) || Properties.ContainsKey(name.Hash()))
				return false;

			SetProperty(new Property(this, name, value, flags));
			return true;
		}

		/// <summary>Read a string property, or <c>null</c> when it was never set.</summary>
		public string GetPropertyString(string name) {
			if (string.IsNullOrEmpty(name) || !TryGetProperty(name.Hash(), out var property) || property == null)
				return null;

			return property.Value switch {
				string str                 => str,
				byte[] { Length: > 0 } raw => Encoding.UTF8.GetString(raw),
				_                          => null,
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

		// Not stored in Properties on purpose: data must never be picked up by the avatar parameter
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

		/// <summary>
		/// Whether a transform sent by <paramref name="senderId"/> must be ignored because the parts
		/// of this entity are authoritative locally (the local player drives its own rig).
		/// </summary>
		public virtual bool IgnoresTransformEcho(uint senderId)
			=> false;

		#endregion

		virtual protected Physicals.Physical InstantiatePhysical() {
			Logger.LogWarning($"Entity {Id} does not implement {nameof(InstantiatePhysical)}, cannot create physical representation.", tag: nameof(Entity));
			return null;
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
				if (!HasPhysical())
					MakePhysical();
				else if (Physical.IsDestroying)
					// Physical exists but is hidden (delayed destroy pending): cancel and re-enable
					Physical.CancelDestroy();
			} else {
				if (HasPhysical() && !Physical.IsDestroying)
					DestroyPhysical();
			}
		}

		public virtual void Tick() {
			// Only a relay-replicated entity has properties to push (a local-only entity never leaves
			// the client).
			if (!IsNetworked)
				return;

			// Check if any properties need to be sent and send them
			// Note: Tick() is already rate-limited by the Room's TPS via Session.Update()
			if (!_startProperties)
				SendPropertiesIfNeeded().Forget();
		}

		public bool HasPhysical()
			=> Physical;

		public bool TryGetPhysical<T>(out T physical) where T : Physical {
			if (Physical is T p) {
				physical = p;
				return true;
			}

			physical = null;
			return false;
		}

		public bool MakePhysical() {
			if (Physical)
				return true;
			Physical = InstantiatePhysical();
			var success = Physical;
			if (success) {
					Physical.ActuallyDestroyed.AddListener(HandlePhysicalActuallyDestroyed);
				OnPhysicalCreated();
			}
			return success;
		}

		private void HandlePhysicalActuallyDestroyed() {
			if (!Physical)
				return;
			Physical = null;
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

		void IEntity.DestroyPhysical()
			=> DestroyPhysical(true);

		public void DestroyPhysical(bool immediate = false) {
			if (!Physical)
				return;

			var physical = Physical;

			if (immediate) {
				Physical = null;
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

		#region Properties Synchronization

		// Prevents concurrent execution of SendPropertiesIfNeeded()
		private bool              _startProperties;
		private readonly List<IProperty>       _dirtyBuffer = new();
		private readonly List<UniTask<bool>>   _taskBuffer  = new();

		/// <summary>
		/// Sends property updates for properties that are dirty and have LocalEmit flag.
		/// Called from Tick() which is already rate-limited by the Room's TPS.
		/// Only sends properties that are marked as dirty and have the appropriate sync flags.
		/// Clears the dirty flag after successful send.
		/// </summary>
		async protected UniTask SendPropertiesIfNeeded() {
			_startProperties = true;

			var room = Context?.Context.Room;
			if (room == null)
				goto end;

			var resendSeconds  = room.PropertyResendInterval;
			var resendEnabled  = resendSeconds > 0;
			var resendInterval = TimeSpan.FromSeconds(resendSeconds);
			var now            = DateTime.UtcNow;

			_dirtyBuffer.Clear();
			foreach (var property in Properties.Values) {
				if (!property.Flags.HasFlag(PropertyFlags.LocalEmit))
					continue;

				// Capture current value once; sets IsDirty if changed
				if (property is AvatarParameterProperty app)
					app.Refresh();

				var overdue = resendEnabled && (now - property.UpdatedAt) >= resendInterval;
				if (!property.IsDirty && !overdue)
					continue;

				_dirtyBuffer.Add(property);
			}

			if (_dirtyBuffer.Count > 0)
				await SendPropertiesBatch(room, _dirtyBuffer);

		end:
			_startProperties = false;
		}

		/// <summary>
		/// Sends a batch of property updates to the room.
		/// Splits properties into chunks if they exceed MaxParameters limit.
		/// Clears the dirty flag for each property after successful send.
		/// </summary>
		/// <param name="room">The room to send properties to</param>
		/// <param name="dirtyProperties">List of dirty properties to send</param>
		private async UniTask SendPropertiesBatch(Core.Rooms.Room room, List<IProperty> dirtyProperties) {
			_taskBuffer.Clear();

			for (var i = 0; i < dirtyProperties.Count; i += PropertiesRequest.MaxParameters) {
				var count   = Math.Min(PropertiesRequest.MaxParameters, dirtyProperties.Count - i);
				// Target this entity itself: a player targets itself, a prop targets the prop id.
				var request = PropertiesRequest.Create(Id, dirtyProperties, i, count);
				_taskBuffer.Add(room.Properties(request));
			}

			var results = await UniTask.WhenAll(_taskBuffer);

			if (results.All(r => r)) {
				foreach (var property in dirtyProperties) {
					property.IsDirty = false;
					if (property is AvatarParameterProperty avatarProp)
						avatarProp.UpdateCache();
				}
			} else {
				Logger.LogWarning($"Failed to send some properties for entity {Id}, will retry on next tick.", tag: nameof(Entity));
			}
		}

		#endregion

		public override string ToString()
			=> $"{GetType().Name}[Id={Id}]";
	}
}