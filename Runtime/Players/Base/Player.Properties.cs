using System.Collections.Generic;
using System.Linq;
using Nox.Avatars.Parameters;
using Nox.Entities;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Relay.Runtime.Players {
	public abstract partial class Player {

		#region Parameter module binding

		private IParameterModule _module;

		/// <summary>Binds a parameter module and follows its registrations; null unbinds and releases.</summary>
		protected internal void Bind(IParameterModule module) {
			// Same module: keep it, its properties are already in sync with it.
			if (module != null && ReferenceEquals(_module, module))
				return;

			Unbind();

			if (module == null)
				return;

			_module = module;
			module.OnRegistred.AddListener(OnParameterRegistered);
			module.OnUnRegistred.AddListener(OnParameterUnregistered);

			SynchronizeAvatarParameters(module.GetParameters(), IsLocal);
		}

		/// <summary>Unbinds the module, stops listening and releases the avatar parameters.</summary>
		protected internal void Unbind() {
			_module?.OnRegistred.RemoveListener(OnParameterRegistered);
			_module?.OnUnRegistred.RemoveListener(OnParameterUnregistered);
			_module = null;
			ReleaseAvatarParameters();
		}

		private void OnParameterRegistered(IParameter parameter)
			=> SynchronizeAvatarParameter(parameter, IsLocal);

		/// <summary>Keeps the slot: values can still arrive from the network while it is unregistered.</summary>
		private void OnParameterUnregistered(IParameter parameter)
			=> ReleaseAvatarParameter(parameter.GetKey());

		#endregion

		#region Common Avatar Parameter Synchronization

		/// <summary>Synchronizes avatar parameters as Entity properties, pruning the ones gone.</summary>
		protected void SynchronizeAvatarParameters(IParameter[] parameters, bool isLocal) {
			var paramKeys = new HashSet<int>();

			if (parameters?.Length > 0)
				foreach (var param in parameters) {
					SynchronizeAvatarParameter(param, isLocal);
					paramKeys.Add(param.GetKey());
				}

			// Remove AvatarParameterProperty entries no longer present in the avatar's parameter list
			foreach (var key in Properties.Keys.ToList())
				if (Properties[key] is AvatarParameterProperty && !paramKeys.Contains(key)) {
					Properties.Remove(key);
					Logger.LogDebug($"Removed avatar parameter property (key={key}) no longer present in avatar.", tag: GetType().Name);
				}
		}

		// Creates, re-binds or refreshes the property for a single parameter. Never prunes: this is
		// also the path taken when a parameter registers on an already bound module.
		private void SynchronizeAvatarParameter(IParameter param, bool isLocal) {
			var flags         = param.GetFlags();
			var propertyFlags = PropertyFlags.None;

			if (flags.HasFlag(ParameterFlags.OwnerSyncsToViewers))
				propertyFlags |= isLocal ? PropertyFlags.LocalEmit : PropertyFlags.RemoteEmit;
			if (flags.HasFlag(ParameterFlags.ViewerSyncsToOwner))
				propertyFlags |= isLocal ? PropertyFlags.RemoteEmit : PropertyFlags.LocalEmit;

			var key = param.GetKey();

			if (Properties.TryGetValue(key, out var existingProp)) {
				if (existingProp is AvatarParameterProperty avatarProp) {
					// The key is stable across avatars, the IParameter instance is not: a property
					// still bound to the previous avatar reads its disposed playable graph.
					if (!ReferenceEquals(avatarProp.Parameter, param)) {
						var reboundProp = new AvatarParameterProperty(this, param, propertyFlags);
						if (propertyFlags.HasFlag(PropertyFlags.LocalEmit))
							reboundProp.IsDirty = true; // send the initial value immediately
						SetProperty(reboundProp);
						Logger.LogDebug($"Rebound property for parameter {param.GetName()} (key={key}, flags={flags}) to the new avatar's parameter.", tag: GetType().Name);
					} else if (!avatarProp.IsDirty) {
						avatarProp.UpdateCache();
					}
				} else if (existingProp is UnassignedProperty) {
					var newProp = new AvatarParameterProperty(this, param, propertyFlags);
					if (propertyFlags.HasFlag(PropertyFlags.LocalEmit))
						newProp.IsDirty = true; // send the initial value immediately
					SetProperty(newProp);
					Logger.LogDebug($"Replaced unassigned property for parameter {param.GetName()} (key={key}, flags={flags}) with propertyFlags={propertyFlags}", tag: GetType().Name);
				}
			} else {
				var newProp = new AvatarParameterProperty(this, param, propertyFlags);
				if (propertyFlags.HasFlag(PropertyFlags.LocalEmit))
					newProp.IsDirty = true; // send the initial value immediately
				SetProperty(newProp);
				Logger.LogDebug($"Created property for parameter {param.GetName()} (key={key}, flags={flags}) with propertyFlags={propertyFlags}", tag: GetType().Name);
			}
		}

		/// <summary>
		/// Converts every <see cref="AvatarParameterProperty"/> into an <see cref="UnassignedProperty"/>,
		/// keeping its last value and its sync direction.
		/// </summary>
		/// <remarks>
		/// The avatar owning those parameters is being destroyed, so reading them would throw
		/// (<c>ArgumentException</c> on a disposed playable, <c>MissingReferenceException</c> on a
		/// destroyed component). The slot survives so values arriving while the next avatar loads
		/// are not dropped. Must run before the next <see cref="SynchronizeAvatarParameters"/>;
		/// <see cref="Unbind"/> takes care of it.
		/// </remarks>
		protected internal void ReleaseAvatarParameters() {
			foreach (var key in Properties.Keys.ToList())
				ReleaseAvatarParameter(key);
		}

		private void ReleaseAvatarParameter(int key) {
			if (!Properties.TryGetValue(key, out var prop) || prop is not AvatarParameterProperty avatarProp)
				return;

			Properties[key] = new UnassignedProperty(this, key, avatarProp.Value, avatarProp.Flags);
			Logger.LogDebug($"Released avatar parameter '{avatarProp.Name}' (key={key}) to an unassigned property; the avatar is gone, its binding is no longer queried.", tag: GetType().Name);
		}

		#endregion

	}
}
