using System;
using Nox.CCK.Utils;
using Nox.Relay.Core.Types.Contents.Rooms;
using Buffer = Nox.CCK.Utils.Buffer;

namespace Nox.Relay.Core.Types.Voice {
	/// <summary>
	/// Sub-types within the <c>Voice(0x14)</c> packet.
	/// </summary>
	public enum VoiceSubType : byte {
		/// <summary>Audio sample: [channel_id:u32][level_flags:u8][sample:bytes]</summary>
		Sample = 0x00,

		/// <summary>Hearing control: [listener_id:u16][speaker_id:u16][control_flags:u8]</summary>
		Control = 0x01,
	}

	/// <summary>
	/// Voice packet sent as a QUIC datagram (sub-type <c>Voice</c>).
	/// After the outer <c>[iid:u8]</c> prefix (added by <c>Room.Emit</c>),
	/// the payload begins with a <see cref="VoiceSubType"/> byte.
	/// <para>
	/// <b>Sample</b> (0x00):
	/// <c>[sub_type:0x00][channel_id:u32][level_flags:u8][sample:bytes...]</c>
	/// </para>
	/// <para>
	/// <b>Control</b> (0x01):
	/// <c>[sub_type:0x01][listener_id:u16][speaker_id:u16][control_flags:u8]</c>
	/// </para>
	/// </summary>
	public class VoiceRequest : RoomRequest {
		/// <summary>Sub-type identifying the payload structure.</summary>
		public VoiceSubType SubType;

		// ── Sample fields ──

		/// <summary>Voice channel identifier (CRC32, e.g. <see cref="ChannelId.Proximity"/>).</summary>
		public uint ChannelId = Nox.Relay.Core.Types.Stream.ChannelId.Voice;

		/// <summary>
		/// Level flags. Bits 0-1: voice level (0=Whisper, 1=Normal, 2=Broadcast).
		/// </summary>
		public byte LevelFlags;

		/// <summary>Opus-encoded audio samples (Sample sub-type).</summary>
		public byte[] Sample = Array.Empty<byte>();

		/// <summary>Convenience: get/set voice level from LevelFlags.</summary>
		public byte VoiceLevel {
			get => (byte)(LevelFlags & 0b_0000_0011);
			set => LevelFlags = (byte)((LevelFlags & 0b_1111_1100) | (value & 0b_0000_0011));
		}

		// ── Control fields ──

		/// <summary>The player who should (or should not) hear (Control sub-type).</summary>
		public ushort ListenerId;

		/// <summary>The player being heard (Control sub-type).</summary>
		public ushort SpeakerId;

		/// <summary>
		/// Control flags. Bit 0: can_hear (1=allow, 0=deny).
		/// </summary>
		public byte ControlFlags;

		/// <summary>Whether hearing is allowed (Control sub-type).</summary>
		public bool CanHear {
			get => (ControlFlags & 0b_0000_0001) != 0;
			set => ControlFlags = value
				? (byte)(ControlFlags | 0b_0000_0001)
				: (byte)(ControlFlags & 0b_1111_1110);
		}

		/// <summary>
		/// Create a Sample sub-type request.
		/// </summary>
		public static VoiceRequest MakeSample(uint channelId, byte levelFlags, byte[] sample) {
			return new VoiceRequest {
				SubType    = VoiceSubType.Sample,
				ChannelId  = channelId,
				LevelFlags = levelFlags,
				Sample     = sample ?? Array.Empty<byte>(),
			};
		}

		/// <summary>
		/// Create a Control sub-type request.
		/// </summary>
		public static VoiceRequest MakeControl(ushort listenerId, ushort speakerId, bool canHear) {
			return new VoiceRequest {
				SubType    = VoiceSubType.Control,
				ListenerId = listenerId,
				SpeakerId  = speakerId,
				CanHear    = canHear,
			};
		}

		public override Buffer ToBuffer() {
			var buffer = new Buffer();
			buffer.Write((byte)SubType);

			switch (SubType) {
				case VoiceSubType.Sample:
					buffer.Write(ChannelId);
					buffer.Write(LevelFlags);
					buffer.Write(Sample);
					break;

				case VoiceSubType.Control:
					buffer.Write(ListenerId);
					buffer.Write(SpeakerId);
					buffer.Write(ControlFlags);
					break;
			}

			return buffer;
		}
	}
}
