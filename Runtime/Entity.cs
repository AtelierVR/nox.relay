using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Nox.Entities;
using Nox.Relay.Core.Types.Properties;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Relay.Runtime {
	public class Entity : IEntity {
		readonly internal Entities Context;
		protected Physicals.Physical Physical;

		readonly internal Dictionary<int, IProperty> Properties = new();

		protected Entity(Entities context, int id) {
			Id      = id;
			Context = context;
			context.RegisterEntity(this);
		}

		public int Id { get; }

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

		virtual protected Nox.Relay.Runtime.Physicals.Physical InstantiatePhysical() {
			Logger.LogWarning($"Entity {Id} does not implement {nameof(InstantiatePhysical)}, cannot create physical representation.", tag: nameof(Entity));
			return null;
		}

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
			Physical.Destroy(immediate);
			if (immediate) {
				// Actual destruction: clear state now (HandlePhysicalActuallyDestroyed won't fire)
				Physical = null;
				OnPhysicalDestroyed();
			}
			// Non-immediate: Physical stays referenced while hidden.
			// HandlePhysicalActuallyDestroyed() will clear it after the delay.
		}

		public virtual void Dispose() {
			DestroyPhysical(true);
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
				var request = PropertiesRequest.Create(ushort.MaxValue, dirtyProperties, i, count);
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