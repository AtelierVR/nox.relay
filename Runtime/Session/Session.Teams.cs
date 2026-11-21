using Nox.CCK.Sessions;
using Nox.Entities;
using Nox.Sessions;
using UnityEngine;
using UnityEngine.Events;

namespace Nox.Relay.Runtime {
	/// <summary>
	/// Team registry of the session: the teams themselves and the relations between them
	/// (see <see cref="ITeamSession"/>).
	/// </summary>
	public sealed partial class Session : ITeamSession {
		private readonly TeamRegistry _teams = new();

		/// <inheritdoc />
		public UnityEvent<ITeam> OnTeamCreated
			=> _teams.OnTeamCreated;

		/// <inheritdoc />
		public UnityEvent<ITeam> OnTeamRemoved
			=> _teams.OnTeamRemoved;

		/// <inheritdoc />
		public UnityEvent<ITeamEntity> OnTeamChanged
			=> _teams.OnTeamChanged;

		/// <inheritdoc />
		public ITeam[] GetTeams()
			=> _teams.GetTeams();

		/// <inheritdoc />
		public ITeam GetTeam(int id)
			=> _teams.GetTeam(id);

		/// <inheritdoc />
		public ITeam CreateTeam(string name, Color color)
			=> _teams.CreateTeam(name, color);

		/// <inheritdoc />
		public bool RemoveTeam(int id)
			=> _teams.RemoveTeam(id);

		/// <summary>
		/// Announces that <paramref name="entity"/> now belongs to another team (or to none), so the listeners
		/// of <see cref="ITeamSession.OnTeamChanged"/> can refresh. Called by the entity itself.
		/// </summary>
		public void NotifyTeamChanged(ITeamEntity entity) {
			_teams.NotifyTeamChanged(entity);
			RefreshPlayerCollisions();
		}

		/// <inheritdoc />
		public TeamRelation GetRelation(ITeam team, ITeam other)
			=> _teams.GetRelation(team, other);

		/// <inheritdoc />
		public void SetRelation(ITeam team, ITeam other, TeamRelation relation) {
			_teams.SetRelation(team, other, relation);
			RefreshPlayerCollisions();
		}

		/// <inheritdoc />
		public bool GetCollision(ITeam team, ITeam other)
			=> _teams.GetCollision(team, other);

		/// <inheritdoc />
		public void SetCollision(ITeam team, ITeam other, bool collide) {
			_teams.SetCollision(team, other, collide);
			RefreshPlayerCollisions();
		}
	}
}
