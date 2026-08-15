using System;
using Nox.Relay.Runtime.Players;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>
	/// Base class for a player's voice pipeline. Derived classes implement either the
	/// local microphone sender (<see cref="LocalVoiceProvider"/>) or the remote playback
	/// receiver (<see cref="RemoteVoiceProvider"/>).
	/// </summary>
	public abstract class VoiceProvider {
		protected readonly Player Player;
		protected Core.Rooms.Room Room;
		protected bool Started;

		private const int InstanceIdSize = 1;          // room iid prefix written by Room.Emit
		private const int StreamSampleHeaderSize = 18; // sub_type(1)+channel(4)+flags(1)+frame_index(4)+timestamp(8)

		protected VoiceProvider(Player player) {
			Player = player;
		}

		/// <summary>Set up (or re-resolve) the voice pipeline. Idempotent.</summary>
		public abstract void Initialize();

		/// <summary>Tear down the voice pipeline and release resources.</summary>
		public abstract void Dispose();

		/// <summary>
		/// Max Opus payload bytes per voice datagram, derived from the connection MTU
		/// minus the fixed protocol overhead (relay header + room iid + stream sample header).
		/// </summary>
		protected int MaxDataBytesPerPacket {
			get {
				ushort mtu = Room.Connection.Connector.Mtu;
				int overhead = Core.Relay.HeaderSize + InstanceIdSize + StreamSampleHeaderSize;
				if (mtu <= overhead)
					throw new InvalidOperationException(
						$"Voice relay MTU is not ready: mtu={mtu} must exceed overhead {overhead}.");
				return mtu - overhead;
			}
		}
	}
}
