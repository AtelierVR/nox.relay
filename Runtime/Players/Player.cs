using System.Linq;
using Nox.Entities;
using Nox.Players;
using Nox.Users;
using Nox.Worlds.Spawns;
using UnityEngine;
using Logger = Nox.CCK.Utils.Logger;
using CorePlayer = Nox.Relay.Core.Players.Player;

namespace Nox.Relay.Runtime.Players {
	public class Player : Entity, IPlayer {
		public Player(Entities context, CorePlayer player) : base(context, player.Id) {
			Reference  = player;
			Identifier = player.Identifier;

			Position = Vector3.zero;
			Rotation = Quaternion.identity;
			Scale    = Vector3.one;
			Velocity = Vector3.zero;
			Angular  = Vector3.zero;
		}

		public CorePlayer Reference;

		public Vector3 Position { get; set; }

		public Quaternion Rotation { get; set; }

		public Vector3 Scale { get; set; }

		public Vector3 Velocity { get; set; }

		public Vector3 Angular { get; set; }

		public IPart[] GetParts() {
			throw new System.NotImplementedException();
		}

		public bool TryGetPart(ushort name, out IPart part) {
			throw new System.NotImplementedException();
		}

		public string Display { get; set; }

		public IUserIdentifier Identifier { get; }

		public bool IsMaster
			=> Context.MasterId == Id;

		public bool IsLocal
			=> Context.LocalId == Id;

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
	}
}