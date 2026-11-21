namespace Nox.Relay.Core.Types.Entities {
	/// <summary>
	/// Operation carried by an <see cref="EntityRequest"/> / <see cref="EntityEvent"/>.
	/// <para>
	/// The relay manages networked entities generically: a client sends an operation for a given
	/// entity type (a prop, a mirror, ...), the relay owns the id and broadcasts the canonical state
	/// back to every player. The ownership is arbitrated by the privileged players through the single
	/// <see cref="Ownership"/> operation, refined by <see cref="EntityOwnership"/>.
	/// </para>
	/// </summary>
	public enum EntityType : byte {
		/// <summary>Create an entity; the relay assigns the id.</summary>
		Create = 0,

		/// <summary>Remove an existing entity.</summary>
		Remove = 2,

		/// <summary>Move an existing entity (transform only, sent as a datagram).</summary>
		Transform = 3,

		/// <summary>Ask the relay for the full entity list of the room.</summary>
		Sync = 4,

		/// <summary>Ask for / grant / refuse the ownership of an entity (see <see cref="EntityOwnership"/>).</summary>
		Ownership = 5,

		/// <summary>The operation could not be performed (relay → caller); <see cref="EntityEvent.Result"/> tells why.</summary>
		Error = 9,
	}

	/// <summary>Sub-operation of <see cref="EntityType.Ownership"/>.</summary>
	public enum EntityOwnership : byte {
		/// <summary>
		/// Client → relay: `[eid][pid]`, <c>pid = 0</c> meaning the sender itself. A privileged player
		/// changes the ownership straight away (everybody receives <see cref="Changed"/>); anybody else
		/// only asks, and receives <see cref="Requested"/> or an <see cref="EntityType.Error"/>.
		/// </summary>
		Change = 0,

		/// <summary>Relay → every player: `[eid][owner]`, the new owner of the entity.</summary>
		Changed = 1,

		/// <summary>
		/// Relay → the privileged players and the requester: `[rid][eid][requester]` — a request the
		/// privileged players may grant with a <see cref="Change"/> naming the requester, or refuse
		/// with <see cref="Refuse"/>.
		/// </summary>
		Requested = 2,

		/// <summary>Privileged player → relay: `[rid]`, the request is refused and the requester receives an error.</summary>
		Refuse = 3,
	}

	/// <summary>Result of an entity operation, answered by the relay to the caller.</summary>
	public enum EntityResult : byte {
		/// <summary>The operation was applied.</summary>
		Ok = 0,

		/// <summary>The player may not do that (no privilege, entity owned by another player).</summary>
		Denied = 1,

		/// <summary>The targeted entity or request does not exist.</summary>
		NotFound = 2,

		/// <summary>The instance has no free entity id left.</summary>
		Overflow = 3,

		/// <summary>Nobody answered the ownership request in time.</summary>
		Timeout = 4,
	}
}
