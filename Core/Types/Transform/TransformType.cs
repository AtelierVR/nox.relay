namespace Nox.Relay.Core.Types.Transform {
	/// <summary>
	/// Type of transform target.
	/// </summary>
	public enum TransformType : byte {
		/// <summary>
		/// Transform by object path.
		/// Is used to move a <see cref="UnityEngine.Transform"/> in the scene hierarchy.
		/// You need to have the permission to move objects in the room for this to work.
		/// </summary>
		ByPath = 0,
		
		/// <summary>
		/// Transform a specific part of an entity (a player rig part, a prop sub-element, ...).
		/// The root of an entity is usually part rig <c>0</c> (see <see cref="Nox.CCK.Entities.EntityRig.Base"/>).
		/// You need to have the owning permission of the entity to move its parts.
		/// And the permission to move others' players in the room.
		/// </summary>
		EntityPart = 1
	}
}