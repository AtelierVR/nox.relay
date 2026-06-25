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

    /// <summary>
    /// Level flags byte for stream sample packets (wire format).
    /// <para>Bits 0-1: distance mode.</para>
    /// <para>Bit 2: HasGroup.</para>
    /// <para>Bits 3-7: reserved.</para>
    /// </summary>
    [System.Flags]
    public enum StreamLevelFlags : byte
    {
        DistanceMode_Normal = 0,

        Normal = DistanceMode_Normal,
        DistanceMode_Whisper = 1 << 0,
        Whisper = DistanceMode_Whisper,
        DistanceMode_Broadcast = 1 << 1,
        Broadcast = DistanceMode_Broadcast,
        DistanceMode_Mask = DistanceMode_Whisper | DistanceMode_Broadcast,

        HasGroup = 1 << 2,
    }
}
