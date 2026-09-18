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
		private bool   _dead;

		public AvatarParameterProperty(Entity context, IParameter parameter, PropertyFlags flags) {
			_parameter     = parameter ?? throw new ArgumentNullException(nameof(parameter));
			Key            = parameter.GetKey();
			Name           = parameter.GetName();
			Flags          = flags;
			_cachedValue   = SafeGet();
			_refreshedValue = _cachedValue;
			UpdatedAt      = DateTime.UtcNow;
		}

		public int Key { get; }
		public DateTime UpdatedAt { get; private set; }
		public string Name { get; }
		public PropertyFlags Flags { get; }

		/// <summary>
		/// Vrai si la propriété est encore liée au paramètre donné.
		/// Faux après un swap d'avatar : le module a été détruit et le paramètre qui porte
		/// l'état est une nouvelle instance, la propriété doit être rebindée.
		/// </summary>
		internal bool IsBoundTo(IParameter parameter)
			=> !_dead && ReferenceEquals(_parameter, parameter);

		/// <summary>
		/// Reads the parameter, marking the property dead when its backing module was
		/// destroyed (avatar swap/teardown) instead of letting the exception escape.
		/// </summary>
		private object SafeGet() {
			if (_parameter == null || _dead)
				return _refreshedValue;

			try {
				return _parameter.Get();
			} catch (Exception e) when (e is UnityEngine.MissingReferenceException || e is NullReferenceException) {
				// Le module (MonoBehaviour) qui porte ce paramètre a été détruit — avatar
				// remplacé/détruit. La propriété est remplacée au prochain
				// SynchronizeAvatarParameters() ; en attendant on ne l'interroge plus.
				_dead = true;
				return _refreshedValue;
			}
		}

		public object Value {
			get => _refreshedValue;
			set {
				if (_parameter == null || _dead)
					return;

				try {
					_parameter.Set(value);
				} catch (Exception e) when (e is UnityEngine.MissingReferenceException || e is NullReferenceException) {
					_dead = true;
					return;
				}

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
			if (_parameter == null || _dead) return;

			var value = SafeGet();
			if (_dead) return;

			_refreshedValue = value;
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
			try {
				_parameter.Set(data);
			} catch	{
				// ignore
			}

			object converted;
			try   { 
				converted = _parameter.Get(); 
			} catch { 
				converted = data;
			}

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