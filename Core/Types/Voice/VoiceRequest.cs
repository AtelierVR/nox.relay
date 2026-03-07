using System;
using Nox.CCK.Utils;
using Nox.Relay.Core.Types.Contents.Rooms;
using Buffer = Nox.CCK.Utils.Buffer;

namespace Nox.Relay.Core.Types.Voice {
	/// <summary>
	/// Voice audio packet sent by the local player to the relay server as a QUIC datagram.
	/// The server broadcasts a <see cref="VoiceEvent"/> to all other players in the instance.
	/// <para>
	/// Wire format (after outer <c>[iid:u8]</c> prefix added by <c>Room.Emit</c>):
	/// <code>[player_id:u16][sample:bytes…]</code>
	/// </para>
	/// <para>
	/// The <c>player_id</c> field is informational — the server always uses its own authoritative
	/// player ID when broadcasting the audio to peers.
	/// </para>
	/// <para>
	/// <b>Datagram constraint:</b> The total packet (header + payload) must not exceed the negotiated MTU.
	/// The sample length must be even (16-bit PCM interleaved samples).
	/// </para>
	/// </summary>
	public class VoiceRequest : RoomRequest {
		/// <summary>
		/// The local player's ID (used as a hint; server overrides this from its own state).
		/// </summary>
		public ushort PlayerId = ushort.MaxValue;

		/// <summary>
		/// Raw PCM audio samples (16-bit interleaved, even number of bytes).
		/// </summary>
		public byte[] Sample = Array.Empty<byte>();

		public override Buffer ToBuffer() {
			var buffer = new Buffer();
			buffer.Write(PlayerId);
			buffer.Write(Sample);
			return buffer;
		}
	}
}
