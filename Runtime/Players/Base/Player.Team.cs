using Nox.Avatars.Parameters;
using Nox.CCK.Entities;
using Nox.Entities;
using Nox.Sessions;
using UnityEngine;

namespace Nox.Relay.Runtime.Players {
	/// <summary>
	/// <see cref="ITeamEntity"/> state of a player: only the team identifier lives in the entity data
	/// container (key <c>team</c>, <c>0</c> meaning none); the team itself is resolved from the
	/// session registry (<see cref="ITeamSession"/>), which the relay session implements.
	/// </summary>
	public abstract partial class Player {
		#region ITeamEntity

		/// <inheritdoc />
		public ITeam Team {
			get {
				var id = Data.Get("team", EntityConstants.NoTeam);
				return id == EntityConstants.NoTeam
					? null
					: (Context.Context as ITeamSession)?.GetTeam(id);
			}
			set {
				var previous = Team;
				var id       = value?.Id ?? EntityConstants.NoTeam;
				Data.Set("team", id);
				if (previous?.Id == id)
					return;
				Context.Context?.NotifyTeamChanged(this);
			}
		}

		#endregion

		#region Team avatar parameters

		/// <summary>Avatar parameter telling whether the player belongs to a team.</summary>
		private const string TeamAssignedParameter = "Team/Assigned";

		/// <summary>Avatar parameter carrying the team colour (a packed ARGB int in the Animator).</summary>
		private const string TeamColorParameter = "Team/Color";

		/// <summary>Avatar parameter carrying the relation of the team to the local team.</summary>
		private const string TeamRelationParameter = "Team/Relation";

		/// <summary>Entity data key carrying the team identifier.</summary>
		private const string TeamDataKey = "team";

		/// <summary>Dispatches the entity data changes: only the team is of interest here.</summary>
		private void OnTeamDataChanged(string[] key, object @new, object @old) {
			if (key is not { Length: 1 } || key[0] != TeamDataKey)
				return;

			// The relation is relative to the local team: every player's value moves with it.
			if (IsLocal) {
				foreach (var player in Context.GetEntities<Player>())
					player.ApplyTeamToAvatar();
				return;
			}

			ApplyTeamToAvatar();
		}

		/// <summary>
		/// Pushes the current team onto the avatar parameters <c>Team/Assigned</c>, <c>Team/Color</c>
		/// and <c>Team/Relation</c> when the loaded avatar declares them (they are read-only from the
		/// relay point of view, like the voice status: the team is a session value, not a parameter
		/// edited by the avatar).
		/// </summary>
		protected void ApplyTeamToAvatar() {
			var module = ParameterModule;
			if (module == null)
				return;

			var team = Team;

			SetBool(module.GetParameter(TeamAssignedParameter), team != null);
			SetColor(module.GetParameter(TeamColorParameter), team?.Color ?? Color.clear);
			SetInt(module.GetParameter(TeamRelationParameter), (int)RelationOf(team));
		}

		/// <summary>
		/// Relation between the team of this player and the team of the local player: <c>Neutral</c>
		/// when either has no team, <c>Ally</c> for the same team, else the session relation matrix.
		/// It is relative to the viewer, so it is never synchronized.
		/// </summary>
		private TeamRelation RelationOf(ITeam team) {
			if (team == null)
				return TeamRelation.Neutral;

			var localTeam = (Context.Context.LocalPlayer as ITeamEntity)?.Team;
			if (localTeam == null)
				return TeamRelation.Neutral;

			if (localTeam.Id == team.Id)
				return TeamRelation.Ally;

			return (Context.Context as ITeamSession)?.GetRelation(localTeam, team) ?? TeamRelation.Neutral;
		}

		private static void SetBool(IParameter parameter, bool value) {
			if (parameter == null || parameter.ValueType != ParameterType.Bool)
				return;

			if (parameter.Value is bool current && current == value)
				return;

			parameter.Value = value;
		}

		private static void SetInt(IParameter parameter, int value) {
			if (parameter == null || parameter.ValueType != ParameterType.Int)
				return;

			if (parameter.Value is int current && current == value)
				return;

			parameter.Value = value;
		}

		/// <summary>
		/// Writes a colour: a complex parameter (<see cref="ParameterType.Color"/>), either a fallback
		/// value or a composite of its scalar components (see the avatar parameter module).
		/// </summary>
		private static void SetColor(IParameter parameter, Color value) {
			if (parameter == null || parameter.ValueType != ParameterType.Color)
				return;

			if (parameter.Value is Color currentColor && currentColor == value)
				return;

			parameter.Value = value;
		}

		#endregion
	}
}
