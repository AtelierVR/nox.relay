namespace Nox.Relay.Runtime {
	public class UnassignedProperty : Property {
		/// <summary>
		/// Creates a new instance of UnassignedProperty
		/// </summary>
		/// <param name="context"></param>
		/// <param name="name"></param>
		/// <param name="value"></param>
		public UnassignedProperty(Entity context, string name, object value) : base(context, name, value) { }

		/// <summary>
		/// Creates a new instance of UnassignedProperty
		/// </summary>
		/// <param name="context"></param>
		/// <param name="key"></param>
		/// <param name="value"></param>
		public UnassignedProperty(Entity context, int key, object value) : base(context, key, value) { }
	}
}