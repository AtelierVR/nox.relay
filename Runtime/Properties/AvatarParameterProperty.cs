using System;
using Nox.Avatars.Parameters;
using Nox.CCK.Network;
using Nox.Entities;
using UnityEngine.Events;

namespace Nox.Relay.Runtime {
	/// <summary>
	/// A property that is linked to an avatar parameter and automatically syncs changes.
	/// </summary>
	public class AvatarParameterProperty : IProperty, IObservableProperty, IDisposable {
		private readonly IParameter _parameter;

		/// <summary>Event of the parameter, or <c>null</c> when it does not announce its writes.</summary>
		private readonly UnityEvent<IParameter> _events;

		private object _cachedValue;
		private object _refreshedValue;

		public AvatarParameterProperty(Entity _, IParameter parameter, PropertyFlags flags) {
			_parameter      = parameter ?? throw new ArgumentNullException(nameof(parameter));
			Key             = parameter.Key;
			Name            = parameter.Name;
			Flags           = flags;
			_cachedValue    = _parameter.Value;
			_refreshedValue = _cachedValue;
			UpdatedAt       = DateTime.UtcNow;

			// The parameter announces its writes: the value is followed without polling it every tick.
			_events = parameter.OnValueChanged;
			_events?.AddListener(OnParameterValueChanged);
		}

		public int Key { get; }
		public DateTime UpdatedAt { get; private set; }
		public string Name { get; }
		public PropertyFlags Flags { get; }

		public UnityEvent<IProperty> OnUpdated { get; } = new();

		/// <summary>Category of the parameter, exposed so the editors can group the properties.</summary>
		public string Category
			=> _parameter.Category;

		/// <summary>The parameter this property is bound to.</summary>
		public IParameter Parameter
			=> _parameter;

		public object Value {
			get => _refreshedValue;
			set {
				_parameter.Value = value;
				_cachedValue    = value;
				_refreshedValue = value;
				UpdatedAt       = DateTime.UtcNow;
				IsDirty         = true;
			}
		}

		/// <summary>
		/// Whether the value is followed through the event of the parameter (see
		/// <see cref="IParameter.OnValueChanged"/>): <see cref="Refresh"/> is then only a fallback for the
		/// parameters that do not announce their writes, so the tick loop can skip this one.
		/// </summary>
		public bool IsObservable
			=> _events != null;

		/// <summary>
		/// Captures the current parameter value once per tick.
		/// Must be called before checking IsDirty or Serialize().
		/// </summary>
		public void Refresh() {
			_refreshedValue = _parameter.Value;
			if (!AreValuesEqual(_refreshedValue, _cachedValue))
				IsDirty = true;
		}

		/// <summary>The parameter was written: its new value is sent on the next tick.</summary>
		private void OnParameterValueChanged(IParameter parameter) {
			_refreshedValue = _parameter.Value;

			if (!AreValuesEqual(_refreshedValue, _cachedValue))
				IsDirty = true;
		}

		/// <summary>Stops following the parameter (the avatar is gone).</summary>
		public void Dispose()
			=> _events?.RemoveListener(OnParameterValueChanged);

		public bool IsDirty { get; set; }

		public byte[] Serialize()
			=> _refreshedValue.ToBytes();

		public void Deserialize(byte[] data) {
			_parameter.Value = data;
			var converted = _parameter.Value;

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
