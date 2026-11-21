using System;
using System.Collections.Generic;
using Nox.CCK.Utils;
using Nox.Entities;
using Nox.Players;
using Nox.Relay.Runtime.Players;
using UnityEngine;

namespace Nox.Relay.Runtime {
	/// <summary>
	/// Body collisions between the players of the session: a player never collides with another one,
	/// unless the two teams they belong to are declared to collide (<see cref="Nox.Sessions.ITeamSession.GetCollision"/>).
	/// A player without a team never collides: the default is "the players walk through each other" and a
	/// world opts in per pair of teams, the pair of a team with itself included (its own members then push
	/// each other).
	/// <para>
	/// The bodies are muted with <see cref="Physics.IgnoreCollision(Collider, Collider, bool)"/> instead of
	/// a layer: the collision has to be decided per pair of teams, which the layer matrix cannot express.
	/// </para>
	/// <para>
	/// The bodies are read from the controller and from the physicals every time the rules are applied, and
	/// never kept here: a body lives with its avatar (its colliders come and go), so the only state of this
	/// part is the pairs currently muted. Refreshes are announced by the events of the session, of the
	/// controller and of the physicals (<see cref="IPhysicalColliders"/>): nothing runs per frame.
	/// </para>
	/// </summary>
	public sealed partial class Session {
		/// <summary>
		/// Pairs of colliders currently muted, with the two colliders they mute so the rule can be lifted
		/// (a body can be culled, or its teams allowed to collide again).
		/// </summary>
		private readonly Dictionary<long, (Collider first, Collider second)> _mutedCollisions = new();

		/// <summary>
		/// Whether the bodies of two players may collide: only when both belong to a team and the two teams
		/// (the same one included) are declared to collide. A player without a team never collides.
		/// </summary>
		public bool CanCollide(IPlayer player, IPlayer other) {
			if (player == null || other == null || player.Id == other.Id)
				return false;

			var team      = player.Team;
			var otherTeam = other.Team;

			if (team == null || otherTeam == null)
				return false;

			return GetCollision(team, otherTeam);
		}

		/// <summary>
		/// Applies the team rules to the physics of the players: the bodies are read again, the pairs that
		/// may not collide are muted and the ones that may are given back their collisions.
		/// </summary>
		internal void RefreshPlayerCollisions() {
			var players = _entities.GetEntities<Player>();
			var local   = Main.ControllerAPI?.Current?.GetColliders() ?? Array.Empty<Collider>();

			var wanted = new Dictionary<long, (Collider first, Collider second)>();
			for (var i = 0; i < players.Length; i++)
				for (var j = i + 1; j < players.Length; j++) {
					if (CanCollide(players[i], players[j]))
						continue;

					var first  = BodyOf(players[i], local);
					var second = BodyOf(players[j], local);

					foreach (var a in first)
						foreach (var b in second) {
							if (!a || !b)
								continue;

							wanted[Pack(a, b)] = (a, b);
						}
				}

			// Gives their collisions back to the pairs that no longer apply (teams changed, body replaced).
			foreach (var pair in new List<long>(_mutedCollisions.Keys)) {
				var (first, second) = _mutedCollisions[pair];
				_mutedCollisions.Remove(pair);

				if (wanted.Remove(pair))
					continue;

				if (first && second)
					Physics.IgnoreCollision(first, second, false);
			}

			foreach (var (first, second) in wanted.Values) {
				_mutedCollisions[Pack(first, second)] = (first, second);
				Physics.IgnoreCollision(first, second, true);
			}
		}

		/// <summary>
		/// Gives the physics back every collision this session muted: the settings it changed must not
		/// outlive it.
		/// </summary>
		internal void ReleasePlayerCollisions() {
			foreach (var (first, second) in _mutedCollisions.Values)
				if (first && second)
					Physics.IgnoreCollision(first, second, false);

			_mutedCollisions.Clear();
		}

		/// <summary>
		/// Colliders of the body of a player: the ones the controller of the local player carries, the ones
		/// of the physical of a remote one.
		/// </summary>
		private static IReadOnlyList<Collider> BodyOf(Player player, IReadOnlyList<Collider> localBody) {
			if (player.IsLocal)
				return localBody;

			return player.TryGetPhysical(out Physical physical) && physical is IPhysicalColliders body
				? body.Colliders
				: Array.Empty<Collider>();
		}

		/// <summary>Order independent key of a pair of colliders.</summary>
		private static long Pack(Collider first, Collider second) {
			var a = first.GetId();
			var b = second.GetId();

			return a < b
				? ((long)a << 32) | (uint)b
				: ((long)b << 32) | (uint)a;
		}
	}
}
