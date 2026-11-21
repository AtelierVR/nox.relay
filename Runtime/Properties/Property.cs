using System;
using Nox.CCK.Network;
using Nox.Entities;
using UnityEngine.Events;

namespace Nox.Relay.Runtime {
	public class Property : IProperty {
		public Property(IEntity context, string name, object value, PropertyFlags flags = PropertyFlags.None) : this(context, name.Hash(), value, flags)
			=> Name = name;

		public Property(IEntity context, int key, object value, PropertyFlags flags = PropertyFlags.None) {
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

		public UnityEvent<IProperty> OnUpdated { get; } = new();

		/// <summary>A raw property carries no category: the editors show it in the « general » section.</summary>
		public string Category
			=> null;

		public object Value { get; set; }
		public PropertyFlags Flags { get; }
	}
}
