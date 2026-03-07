using Nox.Entities;

namespace Nox.Relay.Runtime {
	public class UnassignedProperty : Property {
		/// <summary>
		/// Creates a new instance of UnassignedProperty with Synced flags by default
		/// to allow receiving properties from remote entities.
		/// </summary>
		/// <param name="context"></param>
		/// <param name="name"></param>
		/// <param name="value"></param>
		/// <param name="flags"></param>
		public UnassignedProperty(Entity context, string name, object value, PropertyFlags flags = PropertyFlags.Synced) 
			: base(context, name, value, flags) { }

		/// <summary>
		/// Creates a new instance of UnassignedProperty with Synced flags by default
		/// to allow receiving properties from remote entities.
		/// </summary>
		/// <param name="context"></param>
		/// <param name="key"></param>
		/// <param name="value"></param>
		/// <param name="flags"></param>
		public UnassignedProperty(Entity context, int key, object value, PropertyFlags flags = PropertyFlags.Synced) 
			: base(context, key, value, flags) { }
	}
}