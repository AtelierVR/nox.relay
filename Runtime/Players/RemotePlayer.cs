using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using Nox.Avatars;
using Nox.Avatars.Parameters;
using Nox.CCK.Players;
using Nox.CCK.Sessions;
using Nox.CCK.Utils;
using Nox.Entities;
using Nox.Relay.Runtime.Physicals;
using Nox.Relay.Runtime.Voice;
using UnityEngine;
using CorePlayer = Nox.Relay.Core.Players.Player;
using Logger = Nox.CCK.Utils.Logger;
using Object = UnityEngine.Object;

namespace Nox.Relay.Runtime.Players {
	public class RemotePlayer : Player {
		public RemotePlayer(Entities context, CorePlayer player) : base(context, player) {
			// Initialize default parts for a basic player rig
			InitializeDefaultParts();
			VoiceProvider = new RemoteVoiceProvider(this);
		}

		protected override IPart CreatePart(ushort index)
			=> new RemotePart(this, index);

		public override bool IsLocal
			=> false;

		private void InitializeDefaultParts() {
			// Create base part at minimum
			if (!Parts.ContainsKey(PlayerRig.Base.ToIndex()))
				Parts[PlayerRig.Base.ToIndex()] = new RemotePart(this, PlayerRig.Base.ToIndex());
		}

		override protected bool IsVisible
			=> Vector3.Distance(Position, Context.LocalPlayer.Position)
				< Mathf.Min(Context.Context.Room.RenderEntity, Settings.RenderEntityDistance);

		override protected Physicals.Physical InstantiatePhysical() {
			var asset = Main.CoreAPI.AssetAPI.GetAsset<GameObject>("remote_physical.prefab");
			if (!asset) {
				Logger.LogError("Failed to load remote physical prefab");
				return null;
			}

			var instance = asset.Instantiate();
			instance.name = $"{typeof(RemotePhysical)}_{Id}";

			var physical = instance.GetComponent<RemotePhysical>();
			if (!physical) {
				Logger.LogError("Remote physical prefab is missing RemotePhysical component");
				instance.Destroy();
				return null;
			}

			physical.Reference = this;
			Object.DontDestroyOnLoad(instance);
			return physical;
		}

		/// <summary>
		/// Called after the physical representation is created.
		/// </summary>
		/// <remarks>
		/// The avatar itself is applied by <see cref="Physicals.RemotePhysical.Setup"/>, which runs
		/// as soon as <c>Reference</c> is assigned in <see cref="InstantiatePhysical"/> (and on
		/// enable): it shows the error avatar when the announced identifier is unusable, otherwise
		/// the loading placeholder followed by the announced avatar once it is resolved.
		/// </remarks>
		override protected void OnPhysicalCreated() {
			base.OnPhysicalCreated();

			// Set up voice on the new physical
			VoiceProvider.Initialize();
		}

		override protected void OnPhysicalDestroyed() {
			VoiceProvider.Dispose();
			base.OnPhysicalDestroyed();
		}

		/// <summary>
		/// Update or create a part for this remote player
		/// </summary>
		internal void UpdatePart(ushort partId, IPart partData) {
			if (!Parts.TryGetValue(partId, out var part)) {
				part          = new RemotePart(this, partId);
				Parts[partId] = part;
			}

			// Update the part's transform data
			if (partData.Position != part.Position)
				part.Position = partData.Position;
			if (partData.Rotation != part.Rotation)
				part.Rotation = partData.Rotation;
			if (partData.Scale != part.Scale)
				part.Scale = partData.Scale;
			if (partData.Velocity != part.Velocity)
				part.Velocity = partData.Velocity;
			if (partData.Angular != part.Angular)
				part.Angular = partData.Angular;
		}

		public override async UniTask<bool> SetAvatar(Identifier identifier) {
			Logger.LogDebug($"Changing avatar for {this} to {identifier.ToString()}", tag: nameof(RemotePlayer));

			Avatar = identifier;

			if (!HasPhysical()) {
				// No physical yet, but we still need to prepare properties for when avatar loads
				Logger.LogDebug($"No physical yet for RemotePlayer {Id}, avatar will be set when physical is created", tag: nameof(RemotePlayer));
				return true;
			}

			if (Physical is not RemotePhysical physical)
				return false;

			var result = await physical.SetAvatar(identifier);
			if (result == null)
				return false;

			// The avatar's parameter properties were declared by RemotePhysical.SetAvatar(IRuntimeAvatar)
			// when it attached the avatar — binding again here would duplicate the sync pass.

			// VoiceProvider.Initialize() is a one-shot (it returns immediately once Started), so this
			// is not an avatar-change hook: it only covers the case where the first attempt — from
			// OnPhysicalCreated() — bailed out with "Physical not ready". The avatar-driven output
			// migration is done by the provider itself, which listens to the physical's OnAvatarSet
			// (RemoteVoiceProvider.OnAvatarSet → CreateOrMigrateOutput), the same attach point that
			// now declares the avatar parameters.
			VoiceProvider.Initialize();

			return true;
		}

		/// <summary>
		/// Initializes avatar parameter properties for remote player to receive updates.
		/// Uses the base class SynchronizeAvatarParameters() method.
		/// </summary>
		/// <remarks>
		/// Called by <see cref="RemotePhysical.SetAvatar(IRuntimeAvatar)"/> when it attaches
		/// an avatar, whatever the path that produced it: an announcement received while this player
		/// already has a physical, one received before the physical existed (<see cref="SetAvatar(Identifier)"/>
		/// returns early in that case and the avatar is applied by <see cref="Physicals.RemotePhysical.Setup"/>),
		/// the loading placeholder or the error avatar. Without it, an avatar can be visible while the
		/// owner's synchronized values (VelocityX/VelocityZ, …) land in an UnassignedProperty and the
		/// avatar never animates.
		/// </remarks>
		/// <param name="avatar">The runtime avatar instance</param>
		internal void InitializeAvatarParameters(IRuntimeAvatar avatar) {
			var descriptor = avatar?.Descriptor;
			var parameterModule = descriptor?.GetModules<IParameterModule>().FirstOrDefault();
			var parameters = parameterModule?.GetParameters() ?? Array.Empty<IParameter>();
			SynchronizeAvatarParameters(parameters, isLocal: false);
		}
	}
}