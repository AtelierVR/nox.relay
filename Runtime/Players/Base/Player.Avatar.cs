using System.Linq;
using Nox.Avatars;
using Nox.Avatars.Parameters;
using Nox.CCK.Events;
using Nox.Entities;

namespace Nox.Relay.Runtime.Players {
	public abstract partial class Player {

		/// <summary>
		/// Fired once an avatar is loaded and bound on this player: by <see cref="LocalPlayer"/> after
		/// the room announcement, and when RemotePhysical attaches an avatar for a remote player.
		/// </summary>
		public readonly NoxEvent<IRuntimeAvatar> OnAvatarLoaded = new();

		/// <summary>
		/// Avatar loaded/attached hook: binds the avatar's parameter module, so the values its owner
		/// synchronizes land on a bound IParameter instead of an UnassignedProperty, then raises
		/// <see cref="OnAvatarLoaded"/>. Unbinds when the avatar carries no module.
		/// </summary>
		/// <remarks>
		/// Shared attach point: <see cref="LocalPlayer"/> overrides it to announce the avatar to the
		/// room first, and <c>RemotePhysical.SetAvatar(IRuntimeAvatar)</c> calls it for remote
		/// players, whatever the path that produced the avatar (owner announcement received before
		/// or after the physical existed, loading placeholder, error avatar).
		/// </remarks>
		internal virtual void UpdateAvatar(IRuntimeAvatar avatar) {
			var module = avatar?.Descriptor
				?.GetModules<IParameterModule>()
				.FirstOrDefault();
			Bind(module);

			if (avatar == null)
				return;

			AnnounceLocalProperties();
			OnAvatarLoaded.Invoke(avatar);
		}

		/// <summary>
		/// Marks the properties this client feeds as dirty, so their current value leaves again: they are only
		/// sent when they change, and an avatar measured/sized after the binding (its size, its height) would
		/// otherwise never be announced.
		/// </summary>
		private void AnnounceLocalProperties() {
			if (!IsLocal)
				return;

			foreach (var property in Properties)
				if (property.Flags.HasFlag(PropertyFlags.LocalEmit))
					property.IsDirty = true;
		}

	}
}
