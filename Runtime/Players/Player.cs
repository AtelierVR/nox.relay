using System.Collections.Generic;
using System.Linq;
using Nox.CCK.Players;
using Nox.Entities;
using Nox.Players;
using Nox.Users;
using Nox.Worlds.Spawns;
using UnityEngine;
using Logger = Nox.CCK.Utils.Logger;
using CorePlayer = Nox.Relay.Core.Players.Player;

namespace Nox.Relay.Runtime.Players {
	public abstract class Player : Entity, IPlayer {
		public Player(Entities context, CorePlayer player) : base(context, player.Id) {
			Reference  = player;
			Identifier = player.Identifier;
		}

	public readonly CorePlayer Reference;

	internal readonly Dictionary<ushort, IPart> _parts = new();

		public IPart[] GetParts()
			=> _parts.Values.ToArray();

		public bool TryGetPart(ushort name, out IPart part) {
			if (_parts.TryGetValue(name, out var p)) {
				part = p;
				return true;
			}

			part = null;
			return false;
		}

		public string Display {
			get => Reference?.Display ?? $"Player #{Id}";
			set {
				if (Reference != null)
					Reference.Display = value;
				// TODO: Send update to server
			}
		}

		public IUserIdentifier Identifier { get; }

		public bool IsMaster
			=> Context.MasterId == Id;

		public abstract bool IsLocal { get; }

		public void Teleport(Vector3 position, Quaternion rotation) {
			Position = position;
			Rotation = rotation;
			Velocity = Vector3.zero;
			Angular  = Vector3.zero;
		}

		public void Respawn() {
			if (Context.Context.Dimensions
				    .GetDescriptor(Dimensions.MainIndex)
				    .GetModules()
				    .FirstOrDefault(e => e is ISpawnModule)
			    is not ISpawnModule module) {
				Logger.LogWarning("No spawn module found for respawning player.");
				return;
			}

			var spawn = module.ChoiceSpawn();
			Teleport(spawn.Position, spawn.Rotation);
			Logger.Log($"Player {Id} respawned to {spawn.Position}.");
		}

		public Vector3 Position {
			get => _parts.TryGetValue(PlayerRig.Base.ToIndex(), out var p) ? p.Position : Vector3.zero;
			set {
				if (!_parts.TryGetValue(PlayerRig.Base.ToIndex(), out var p)) return;
				p.Position = value;
			}
		}

		public Quaternion Rotation {
			get => _parts.TryGetValue(PlayerRig.Base.ToIndex(), out var p) ? p.Rotation : Quaternion.identity;
			set {
				if (!_parts.TryGetValue(PlayerRig.Base.ToIndex(), out var p)) return;
				p.Rotation = value;
			}
		}

		public Vector3 Scale {
			get => _parts.TryGetValue(PlayerRig.Base.ToIndex(), out var p) ? p.Scale : Vector3.one;
			set {
				if (!_parts.TryGetValue(PlayerRig.Base.ToIndex(), out var p)) return;
				p.Scale = value;
			}
		}

		public Vector3 Velocity {
			get => _parts.TryGetValue(PlayerRig.Base.ToIndex(), out var p) ? p.Velocity : Vector3.zero;
			set {
				if (!_parts.TryGetValue(PlayerRig.Base.ToIndex(), out var p)) return;
				p.Velocity = value;
			}
		}

		public Vector3 Angular {
			get => _parts.TryGetValue(PlayerRig.Base.ToIndex(), out var p) ? p.Angular : Vector3.zero;
			set {
				if (!_parts.TryGetValue(PlayerRig.Base.ToIndex(), out var p)) return;
				p.Angular = value;
			}
		}

	public override string ToString() {
		try {
			return $"{GetType().Name}[Id={Id}, Display={Display}, Identifier={Identifier?.ToString() ?? "null"}, IsMaster={IsMaster}, IsLocal={IsLocal}]";
		}
		catch {
			// During construction, some properties may not be initialized yet
			return $"{GetType().Name}[Id={Id}, <initializing>]";
		}
	}
	}
}