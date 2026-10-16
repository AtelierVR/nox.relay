namespace Nox.Relay.Core.Types.Entities {
	/// <summary>
	/// Operation carried by an <see cref="EntityRequest"/> / <see cref="EntityEvent"/>.
	/// <para>
	/// The relay manages networked entities generically: a client sends an operation for a given
	/// entity <c>Kind</c> (a prop, a mirror, ...), the relay owns the id and broadcasts the canonical
	/// state back to every player.
	/// </para>
	/// </summary>
	public enum EntityType : byte {
		/// <summary>Create an entity; the relay assigns the id.</summary>
		Create = 0,

		/// <summary>Update an existing entity (flags + payload).</summary>
		Update = 1,

		/// <summary>Remove an existing entity.</summary>
		Remove = 2,

		/// <summary>Move an existing entity (transform only, sent as a datagram).</summary>
		Transform = 3,

		/// <summary>Ask the relay for the full entity list of the room.</summary>
		Sync = 4,
	}
}
