namespace Nox.Relay.Core.Types.Traveling {
	/// <summary>
	/// Enum representing different traveling actions a local player can take.
	/// </summary>
	public enum TravelingAction : byte {
		/// <summary>
		/// Action who the player use to request the current dimension.
		/// </summary>
		Travel = 0,
		
		/// <summary>
		/// Action who the player use to notify that he is ready in the current dimension.
		/// </summary>
		Ready  = 1,
		
		/// <summary>
		/// Action who the player use to notify that the travel has failed.
		/// </summary>
		Failed  = 2
	}
}