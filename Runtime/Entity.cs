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
		protected Nox.Relay.Runtime.Physicals.Physical Physical;

		readonly internal Dictionary<int, IProperty> Properties = new();

		protected Entity(Entities context, int id) {
			Id      = id;
			Context = context;
			context.RegisterEntity(this);
		}

		public int Id { get; }

		public IProperty[] GetProperties()
			=> Properties.Values.ToArray<IProperty>();

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
			switch (IsVisible) {
				// instantiate physical if needed
				case true when !HasPhysical():
					MakePhysical();
					break;
				// destroy physical if not needed
				case false when HasPhysical():
					DestroyPhysical();
					break;
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
			if (success)
				OnPhysicalCreated();
			return success;
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
			Physical = null;
			OnPhysicalDestroyed();
		}

		public void Dispose() {
			DestroyPhysical();
			Context.UnregisterEntity(this);
		}

		#region Properties Synchronization

		// Prevents concurrent execution of SendPropertiesIfNeeded()
		private bool _startProperties;

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

			// Collect properties that need updates (marked as dirty with LocalEmit flag)
			var dirtyProperties = new List<IProperty>();

			foreach (var property in Properties.Values) {
				// Skip properties that are not dirty
				if (!property.IsDirty)
					continue;

				// Skip properties that don't have LocalEmit flag (not meant to be sent from local)
				if (!property.Flags.HasFlag(PropertyFlags.LocalEmit))
					continue;

				dirtyProperties.Add(property);
			}

			// Send all dirty properties in a batch
			if (dirtyProperties.Count > 0) 
				await SendPropertiesBatch(room, dirtyProperties);

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
			var tasks = new List<UniTask<bool>>();
			Logger.LogDebug($"Sending {dirtyProperties.Count} properties for entity {Id} in {Math.Ceiling((double)dirtyProperties.Count / PropertiesRequest.MaxParameters)} batch(es).", tag: nameof(Entity));

			// Split into chunks to respect MaxParameters limit
			for (var i = 0; i < dirtyProperties.Count; i += PropertiesRequest.MaxParameters) {
				var chunk = dirtyProperties
					.Skip(i)
					.Take(PropertiesRequest.MaxParameters)
					.ToArray();

				// Use ushort.MaxValue to indicate self entity
				var request = PropertiesRequest.Create(ushort.MaxValue, chunk);
				tasks.Add(room.Properties(request));
			}

			// Wait for all network operations to complete
			var results = await UniTask.WhenAll(tasks);

			// Clear dirty flag only if all requests succeeded
			if (results.All(r => r)) {
				foreach (var property in dirtyProperties) {
					property.IsDirty = false;

					// Update cache for AvatarParameterProperty to prevent re-sending
					if (property is AvatarParameterProperty avatarProp) {
						avatarProp.UpdateCache();
					}
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