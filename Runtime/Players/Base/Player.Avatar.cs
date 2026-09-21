using System.Linq;
using Nox.Avatars;
using Nox.Avatars.Parameters;
using Nox.CCK.Events;

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

			// A null avatar is a teardown (unbind), not a load: nothing to announce.
			if (avatar != null)
				OnAvatarLoaded.Invoke(avatar);
		}

	}
}
