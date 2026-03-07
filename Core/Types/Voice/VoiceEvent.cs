using System;
using Nox.CCK.Utils;
using Nox.Relay.Core.Types.Content.Rooms;
using Buffer = Nox.CCK.Utils.Buffer;

namespace Nox.Relay.Core.Types.Voice {
	/// <summary>
	/// Server broadcast event carrying voice audio from a remote player.
	/// Received as a QUIC datagram (after the server strips the sender filter).
	/// <para>
	/// Wire format (after outer <c>[iid:u8]</c> is stripped):
	/// <code>[player_id:u16][sample:bytes…]</code>
	/// </para>
	/// </summary>
	public class VoiceEvent : RoomResponse {
		/// <summary>
		/// The player who sent the audio.
		/// </summary>
		public ushort PlayerId;

		/// <summary>
		/// Raw PCM audio samples (16-bit interleaved).
		/// </summary>
		public byte[] Sample = Array.Empty<byte>();

		public override bool FromBuffer(Buffer buffer) {
			buffer.Start();

			PlayerId = buffer.ReadUShort();
			// Sample occupies all remaining bytes — no length prefix on datagrams.
			var remaining = (ushort)buffer.Remaining;
			Sample = remaining > 0
				? buffer.ReadBytes(remaining)
				: Array.Empty<byte>();

			return true;
		}

		public override string ToString()
			=> $"{GetType().Name}[PlayerId={PlayerId}, SampleBytes={Sample.Length}]";
	}
}
