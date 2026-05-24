using System;
using Nox.Avatars.Parameters;
using Nox.CCK.Network;
using Nox.Entities;

namespace Nox.Relay.Runtime {
	/// <summary>
	/// A property that is linked to an avatar parameter and automatically syncs changes.
	/// </summary>
	public class AvatarParameterProperty : IProperty {
		private readonly IParameter _parameter;
		private object _cachedValue;
		private object _refreshedValue;
		private bool   _isDirty;

		public AvatarParameterProperty(Entity context, IParameter parameter, PropertyFlags flags) {
			_parameter     = parameter ?? throw new ArgumentNullException(nameof(parameter));
			Key            = parameter.GetKey();
			Name           = parameter.GetName();
			Flags          = flags;
			_cachedValue   = parameter.Get();
			_refreshedValue = _cachedValue;
			UpdatedAt      = DateTime.UtcNow;
		}

		public int Key { get; }
		public DateTime UpdatedAt { get; private set; }
		public string Name { get; }
		public PropertyFlags Flags { get; }

		public object Value {
			get => _refreshedValue;
			set {
				if (_parameter == null)
					return;

				_parameter.Set(value);
				_cachedValue    = value;
				_refreshedValue = value;
				UpdatedAt       = DateTime.UtcNow;
				_isDirty        = true;
			}
		}

		/// <summary>
		/// Captures the current parameter value once per tick.
		/// Must be called before checking IsDirty or Serialize().
		/// </summary>
		public void Refresh() {
			if (_parameter == null) return;
			_refreshedValue = _parameter.Get();
			if (!AreValuesEqual(_refreshedValue, _cachedValue))
				_isDirty = true;
		}

		public bool IsDirty {
			get => _isDirty;
			set => _isDirty = value;
		}

		public byte[] Serialize()
			=> _refreshedValue.ToBytes();

		public void Deserialize(byte[] data) {
			_parameter?.Set(data);
			object converted;
			try   { converted = _parameter?.Get() ?? data; }
			catch { converted = data; }
			_cachedValue    = converted;
			_refreshedValue = converted;
			UpdatedAt       = DateTime.UtcNow;
		}

		/// <summary>
		/// Updates the cached value to match the current parameter value.
		/// Call this after successfully sending the property to prevent re-sending.
		/// </summary>
		public void UpdateCache() {
			_cachedValue    = _refreshedValue;
			UpdatedAt       = DateTime.UtcNow;
		}

		private static bool AreValuesEqual(object value1, object value2) {
			if (ReferenceEquals(value1, value2))
				return true;

			if (value1 == null || value2 == null)
				return false;

			// Handle byte arrays specially
			if (value1 is byte[] bytes1 && value2 is byte[] bytes2) {
				if (bytes1.Length != bytes2.Length)
					return false;

				for (var i = 0; i < bytes1.Length; i++)
					if (bytes1[i] != bytes2[i])
						return false;

				return true;
			}

			return value1.Equals(value2);
		}

		public override string ToString()
			=> $"{GetType().Name}[Key={Name ?? Key.ToString()}, Value={Value}, Flags={Flags}, Dirty={IsDirty}]";
	}
}