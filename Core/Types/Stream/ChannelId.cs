namespace Nox.Relay.Core.Types.Stream {
	/// <summary>
	/// Predefined stream channel identifiers.
	/// Channel 0 = voice audio, Channel 1 = video (screen share / camera).
	/// Custom channels can use any uint value > 1.
	/// </summary>
	public static class ChannelId {
		/// <summary>Voice audio (channel 0).</summary>
		public const uint Voice = 0;

		/// <summary>Screen share / camera (channel 1).</summary>
		public const uint Video = 1;
	}
}
