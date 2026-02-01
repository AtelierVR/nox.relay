using System.Collections.Generic;
using System.Linq;
using Nox.CCK.Utils;
using Nox.Entities;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Relay.Runtime {
	public class Entity : IEntity {
		readonly internal Entities Context;
		protected Nox.Relay.Runtime.Physicals.Physical Physical;

		internal readonly Dictionary<int, Property> Properties = new();

		protected Entity(Entities context, int id) {
			Id = id;
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

		public void SetProperty(Property property)
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
		
		public virtual void Tick() {}

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
			if (Physical) return true;
			Physical = InstantiatePhysical();
			return Physical;
		}

		void IEntity.DestroyPhysical()
			=> DestroyPhysical(true);

		public void DestroyPhysical(bool immediate = false) {
			if (!Physical) return;
			Physical.Destroy(immediate);
			Physical = null;
		}

		public void Dispose() {
			DestroyPhysical();
			Context.UnregisterEntity(this);
		}

		public override string ToString()
			=> $"{GetType().Name}[Id={Id}]";

	}
}