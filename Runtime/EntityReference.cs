using Nox.Entities;

namespace Nox.Relay.Runtime {
	/// <summary>
	/// Relay <see cref="Entity"/> wrapping an <see cref="IEntity"/> built by a mod for a spawn the
	/// relay does not know how to build itself (a kind answered through
	/// <see cref="IEntitySpawn"/>). The wrapped entity is exposed as <see cref="Reference"/>.
	/// </summary>
	public sealed class EntityReference : Entity {
		internal EntityReference(Entities context, IEntity reference) : base(context, reference.Id)
			=> Reference = reference;

		/// <summary>The wrapped entity.</summary>
		public IEntity Reference { get; }
	}
}
