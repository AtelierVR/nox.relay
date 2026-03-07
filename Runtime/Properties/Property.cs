using System;
using Nox.CCK.Network;
using Nox.Entities;

namespace Nox.Relay.Runtime {
	public class Property : IProperty {
		public Property(Entity context, string name, object value, PropertyFlags flags = PropertyFlags.None) : this(context, name.Hash(), value, flags)
			=> Name = name;

		public Property(Entity context, int key, object value, PropertyFlags flags = PropertyFlags.None) {
			Key       = key;
			Value     = value;
			UpdatedAt = DateTime.UtcNow;
			Flags     = flags;
		}

		public bool IsDirty { get; set; }

		public byte[] Serialize()
			=> Value.ToBytes();

		public void Deserialize(byte[] data) {
			Value     = data;
			UpdatedAt = DateTime.UtcNow;
		}

		public int Key { get; }
		public DateTime UpdatedAt { get; set; }
		public string Name { get; }
		public object Value { get; set; }
		public PropertyFlags Flags { get; }
	}
}