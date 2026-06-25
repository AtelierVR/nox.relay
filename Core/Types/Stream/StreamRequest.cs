using System;
using Nox.CCK.Utils;
using Nox.Relay.Core.Types.Contents.Rooms;
using Buffer = Nox.CCK.Utils.Buffer;

namespace Nox.Relay.Core.Types.Stream {
	/// <summary>
	/// Sub-types within the <c>Stream(0x14)</c> packet.
	/// </summary>
	public enum StreamSubType : byte {
		/// <summary>Audio/video sample: [channel_id:u32][level_flags:u8][sample:bytes]</summary>
		Sample = 0x00,

		/// <summary>Hearing control: [listener_id:u16][speaker_id:u16][control_flags:u8]</summary>
		Control = 0x01,
	}

	/// <summary>
	/// Stream packet sent as a QUIC datagram (sub-type <c>Stream</c>).
	/// After the outer <c>[iid:u8]</c> prefix (added by <c>Room.Emit</c>),
	/// the payload begins with a <see cref="StreamSubType"/> byte.
	/// <para>
	/// <b>Sample</b> (0x00):
	/// <c>[sub_type:0x00][channel_id:u32][level_flags:u8][?group_id:u16][frame_index:i32][timestamp:f64][sample:bytes…]</c>
	/// </para>
	/// <para>
	/// <b>Control</b> (0x01):
	/// <c>[sub_type:0x01][listener_id:u16][speaker_id:u16][control_flags:u8]</c>
	/// </para>
	/// </summary>
	public class StreamRequest : RoomRequest {
		/// <summary>Sub-type identifying the payload structure.</summary>
		public StreamSubType SubType;

		// ── Sample fields ──

		/// <summary>Local player ID (sent as hint; server uses authenticated ID).</summary>
		public ushort PlayerId;

		/// <summary>Voice channel identifier (CRC32, e.g. <see cref="ChannelId.Proximity"/>).</summary>
		public uint ChannelId = Stream.ChannelId.Voice;

		/// <summary>
	/// Level flags.
	/// <para>Bits 0-1: distance mode (0=Normal, 1=Whisper, 2=Broadcast).</para>
	/// <para>Bit 2: Group flag — when set, <see cref="GroupId"/> follows after this byte.</para>
	/// <para>Bits 3-7: Reserved.</para>
	/// </summary>
		public StreamLevelFlags LevelFlags;

		/// <summary>Group ID (present when <see cref="StreamLevelFlags.HasGroup"/> is set).</summary>
		public ushort GroupId;

		/// <summary>Whether a group ID is present in the sample packet.</summary>
		public bool HasGroup
			=> (LevelFlags & StreamLevelFlags.HasGroup) != 0;

		/// <summary>Opus-encoded audio samples (Sample sub-type).</summary>
		public byte[] Sample = Array.Empty<byte>();

		/// <summary>Monotonically increasing frame index for jitter buffer ordering (Sample sub-type).</summary>
		public int FrameIndex;

		/// <summary>Sender timestamp in seconds (Sample sub-type).</summary>
		public double Timestamp;

		/// <summary>Convenience: get/set distance mode from LevelFlags.</summary>
		public byte DistanceMode {
			set => LevelFlags = (LevelFlags & ~StreamLevelFlags.DistanceMode_Mask) | (StreamLevelFlags)(value & 0b_0000_0011);
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
		/// PlayerId is NOT serialized to the wire — the relay server resolves the
		/// authenticated player from the QUIC connection and injects it in the broadcast.
		/// </summary>
		/// <param name="flags">Distance mode and group flag combined.</param>
		/// <param name="sample">Encoded audio/video sample bytes.</param>
		/// <param name="frameIndex">Monotonically increasing frame index for jitter buffer.</param>
		/// <param name="timestamp">Sender timestamp in seconds.</param>
		/// <param name="groupId">Optional group ID (ushort.MaxValue = no group).</param>
		public static StreamRequest MakeSample(StreamLevelFlags flags, byte[] sample, int frameIndex = 0, double timestamp = 0, ushort groupId = ushort.MaxValue) {
			if (groupId != ushort.MaxValue)
				flags |= StreamLevelFlags.HasGroup;
			return new StreamRequest {
				SubType    = StreamSubType.Sample,
				PlayerId   = 0, // not serialized but kept for local reference
				LevelFlags = flags,
				GroupId    = groupId,
				Sample     = sample ?? Array.Empty<byte>(),
				FrameIndex = frameIndex,
				Timestamp  = timestamp,
			};
		}

		/// <summary>
		/// Create a Control sub-type request.
		/// </summary>
		public static StreamRequest MakeControl(ushort listenerId, ushort speakerId, bool canHear) {
			return new StreamRequest {
				SubType    = StreamSubType.Control,
				ListenerId = listenerId,
				SpeakerId  = speakerId,
				CanHear    = canHear,
			};
		}

		public override Buffer ToBuffer() {
			var buffer = new Buffer();
			buffer.Write((byte)SubType);

			switch (SubType) {
				case StreamSubType.Sample:
					// Note: PlayerId is NOT written — server resolves it from the connection.
					buffer.Write(ChannelId);
					buffer.Write((byte)LevelFlags);
					if (HasGroup)
						buffer.Write(GroupId);
					buffer.Write(FrameIndex);
					buffer.Write(Timestamp);
					buffer.Write(Sample);
					break;

				case StreamSubType.Control:
					buffer.Write(ListenerId);
					buffer.Write(SpeakerId);
					buffer.Write(ControlFlags);
					break;
			}

			return buffer;
		}
	}
}
