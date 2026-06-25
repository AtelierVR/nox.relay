using Nox.Relay.Core.Types.Stream;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>
	/// Voice spatialization mode — maps to the protocol LevelFlags bits 0-1.
	/// <para>0 = Normal (3D spatial, position-based)</para>
	/// <para>1 = Whisper (short-range 3D)</para>
	/// <para>2 = Broadcast (global 2D, no distance attenuation)</para>
	/// </summary>
	public enum VoiceDistanceMode : byte {
		/// <summary>Normal 3D spatial — audio originates from the player's position.</summary>
		Normal = 0,

		/// <summary>Short-range 3D — reduced max distance for private/whisper chat.</summary>
		Whisper = 1,

		/// <summary>
		/// Global 2D broadcast — everyone hears regardless of distance or physical presence.
		/// Uses a dedicated global AudioSource (spatialBlend=0).
		/// </summary>
		Broadcast = 2,
	}

	/// <summary>Static helpers for <see cref="VoiceDistanceMode"/>.</summary>
	public static class VoiceDistanceModeExtensions {
		/// <summary>Extract the distance mode from a LevelFlags byte.</summary>
		public static VoiceDistanceMode FromLevelFlags(byte levelFlags)
			=> (VoiceDistanceMode)(levelFlags & 0b_0000_0011);

		/// <summary>Extract the distance mode from <see cref="StreamLevelFlags"/>.</summary>
		public static VoiceDistanceMode FromLevelFlags(StreamLevelFlags levelFlags)
			=> (VoiceDistanceMode)((byte)levelFlags & 0b_0000_0011);

		/// <summary>Encode a distance mode into <see cref="StreamLevelFlags"/>.</summary>
		public static StreamLevelFlags ToLevelFlags(this VoiceDistanceMode mode, StreamLevelFlags existingFlags = StreamLevelFlags.Normal)
			=> (StreamLevelFlags)(((byte)existingFlags & 0b_1111_1100) | ((byte)mode & 0b_0000_0011));
	}
}
