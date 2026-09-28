using Nox.CCK.Sessions;
using Nox.Entities;
using UnityEngine;

namespace Nox.Relay.Runtime {
	/// <summary>
	/// Team registry of the session: the teams themselves and the relations between them
	/// (see <see cref="ITeamSession"/>).
	/// </summary>
	public sealed partial class Session {
		private readonly TeamRegistry _teams = new();

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

		/// <inheritdoc />
		public TeamRelation GetRelation(ITeam team, ITeam other)
			=> _teams.GetRelation(team, other);

		/// <inheritdoc />
		public void SetRelation(ITeam team, ITeam other, TeamRelation relation)
			=> _teams.SetRelation(team, other, relation);
	}
}
