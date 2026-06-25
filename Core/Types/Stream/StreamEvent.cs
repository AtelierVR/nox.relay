using System;
using Nox.CCK.Utils;
using Nox.Relay.Core.Types.Content.Rooms;
using Buffer = Nox.CCK.Utils.Buffer;

namespace Nox.Relay.Core.Types.Stream {
	/// <summary>
	/// Server broadcast event carrying stream data from a remote player.
	/// Received as a QUIC datagram. The outer <c>[iid:u8]</c> is already
	/// stripped by the time this event is parsed.
	/// <para>
	/// Wire format: <c>[sub_type:u8]</c> followed by sub-type specific fields.
	/// </para>
	/// <para>
	/// <b>Sample</b> (0x00):
	/// <c>[sub_type:0x00][player_id:u16][channel_id:u32][level_flags:u8][sample:bytes…]</c>
	/// </para>
	/// <para>
	/// <b>Control</b> (0x01):
	/// <c>[sub_type:0x01][listener_id:u16][speaker_id:u16][control_flags:u8]</c>
	/// </para>
	/// </summary>
	public class StreamEvent : RoomResponse {
		/// <summary>Sub-type identifying the payload structure.</summary>
		public StreamSubType SubType;

		// ── Sample fields ──

		public ushort PlayerId;
		public uint ChannelId;
		public byte LevelFlags;
		public ushort GroupId;

		/// <summary>Monotonically increasing frame index from sender (Sample sub-type).</summary>
		public int FrameIndex;

		/// <summary>Sender timestamp in seconds (Sample sub-type).</summary>
		public double Timestamp;

		public byte[] Sample = Array.Empty<byte>();

		public bool HasGroup
			=> (LevelFlags & 0b_0000_0100) != 0;

		public byte DistanceMode
			=> (byte)(LevelFlags & 0b_0000_0011);

		// ── Control fields ──

		public ushort ListenerId;
		public ushort SpeakerId;
		public byte ControlFlags;

		public override bool FromBuffer(Buffer buffer) {
			buffer.Start();

			SubType = (StreamSubType)buffer.ReadByte();

			switch (SubType) {
				case StreamSubType.Sample:
					PlayerId   = buffer.ReadUShort();
					ChannelId  = (uint)buffer.ReadInt();
					LevelFlags = buffer.ReadByte();
					if ((LevelFlags & 0b_0000_0100) != 0)
						GroupId = buffer.ReadUShort();
					FrameIndex = buffer.ReadInt();
					Timestamp  = buffer.ReadDouble();
					var remaining = (ushort)buffer.Remaining;
					Sample = remaining > 0
						? buffer.ReadBytes(remaining)
						: Array.Empty<byte>();
					break;

				case StreamSubType.Control:
					ListenerId    = buffer.ReadUShort();
					SpeakerId     = buffer.ReadUShort();
					ControlFlags  = buffer.ReadByte();
					break;
			}

			return true;
		}

		public override string ToString()
			=> SubType switch {
				StreamSubType.Sample =>
					$"{GetType().Name}[Sample Player={PlayerId}, Channel={ChannelId:X8}, Dist={DistanceMode}, Group={GroupId}, SampleBytes={Sample.Length}]",
				StreamSubType.Control =>
					$"{GetType().Name}[Control Listener={ListenerId}, Speaker={SpeakerId}, Flags={ControlFlags}]",
				_ => $"{GetType().Name}[Unknown SubType={SubType}]"
			};
	}
}
