using System;
using Cysharp.Threading.Tasks;
using Nox.Avatars;
using Nox.Avatars.Players;
using Nox.CCK.Avatars;
using Nox.CCK.Players;
using Nox.CCK.Sessions;
using Nox.CCK.Utils;
using Nox.Entities;
using Nox.Relay.Runtime.Physicals;
using UnityEngine;
using CorePlayer = Nox.Relay.Core.Players.Player;
using Logger = Nox.CCK.Utils.Logger;
using Object = UnityEngine.Object;

namespace Nox.Relay.Runtime.Players {
	public class RemotePlayer : Player {
		public RemotePlayer(Entities context, CorePlayer player) : base(context, player) {
			// Initialize default parts for a basic player rig
			InitializeDefaultParts();
		}

		public override bool IsLocal => false;

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

			var instance = Object.Instantiate(asset);
			if (!instance) {
				Logger.LogError("Failed to instantiate remote physical prefab");
				return null;
			}

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
		/// Update or create a part for this remote player
		/// </summary>
		internal void UpdatePart(ushort partId, IPart partData) {
			if (!Parts.TryGetValue(partId, out var part)) {
				part = new RemotePart(this, partId);
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

		public override async UniTask<bool> SetAvatar(IAvatarIdentifier identifier) {
			Logger.LogDebug($"Changing avatar for {this} to {identifier?.ToString() ?? "null"}", tag: nameof(RemotePlayer));

			if (!HasPhysical()) {
				Avatar = identifier;
				return true;
			}
			
			if (Physical is not RemotePhysical physical)
				return false;

			var result = await physical.SetAvatar(identifier);
			if (result == null)
				return false;

			Avatar = identifier;
			return true;
		}
	}
}