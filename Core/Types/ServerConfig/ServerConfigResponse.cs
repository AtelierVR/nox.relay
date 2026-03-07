using Nox.CCK.Utils;
using Nox.Relay.Core.Types.Content.Rooms;
using Nox.Relay.Core.Types.Rooms;
using Buffer = Nox.CCK.Utils.Buffer;

namespace Nox.Relay.Core.Types.ServerConfig {
	/// <summary>
	/// Response to a <see cref="ServerConfigRequest"/>, and server broadcast
	/// (<see cref="ServerConfigResult.Change"/>) when privileged users change the instance configuration.
	/// <para>
	/// Wire format (after outer <c>[iid:u8]</c> is stripped):
	/// <code>[result:u8][flags:u8][tps:u8?][threshold:f32?][capacity:u16?][password_set:u8?][instance_flags:u32?][min_tps:u8?][max_tps:u8?][lb_enabled:u8?]</code>
	/// </para>
	/// </summary>
	public class ServerConfigResponse : RoomResponse {
		/// <summary>Outcome of the change request, or <see cref="ServerConfigResult.Change"/> for a broadcast.</summary>
		public ServerConfigResult Result;

		/// <summary>Which fields are present in this packet.</summary>
		public ServerConfigFlags Flags;

		// ── Field values (populated according to <see cref="Flags"/>) ──────────

		/// <summary>Current TPS (present when <see cref="Flags"/> includes <see cref="ServerConfigFlags.TPS"/>).</summary>
		public byte Tps;

		/// <summary>Current transform threshold (present when <see cref="Flags"/> includes <see cref="ServerConfigFlags.Threshold"/>).</summary>
		public float Threshold;

		/// <summary>Current player capacity (present when <see cref="Flags"/> includes <see cref="ServerConfigFlags.Capacity"/>).</summary>
		public ushort Capacity;

		/// <summary>Whether a password is currently set (present when <see cref="Flags"/> includes <see cref="ServerConfigFlags.Password"/>).</summary>
		public bool PasswordSet;

		/// <summary>Current instance flags (present when <see cref="Flags"/> includes <see cref="ServerConfigFlags.Flags"/>).</summary>
		public RoomFlags InstanceFlags;

		/// <summary>Minimum TPS allowed (present when <see cref="Flags"/> includes <see cref="ServerConfigFlags.MinTPS"/>).</summary>
		public byte MinTps;

		/// <summary>Maximum TPS allowed (present when <see cref="Flags"/> includes <see cref="ServerConfigFlags.MaxTPS"/>).</summary>
		public byte MaxTps;

		/// <summary>Whether load-balancing is enabled (present when <see cref="Flags"/> includes <see cref="ServerConfigFlags.LoadBalancing"/>).</summary>
		public bool LoadBalancingEnabled;

		/// <summary><c>true</c> when the operation was rejected.</summary>
		public bool IsError => Result == ServerConfigResult.Failure;

		public override bool FromBuffer(Buffer buffer) {
			buffer.Start();

			Result = buffer.ReadEnum<ServerConfigResult>();
			Flags  = buffer.ReadEnum<ServerConfigFlags>();

			if (Flags.HasFlag(ServerConfigFlags.TPS))          Tps                   = buffer.ReadByte();
			if (Flags.HasFlag(ServerConfigFlags.Threshold))    Threshold             = buffer.ReadFloat();
			if (Flags.HasFlag(ServerConfigFlags.Capacity))     Capacity              = buffer.ReadUShort();
			if (Flags.HasFlag(ServerConfigFlags.Password))     PasswordSet           = buffer.ReadByte() != 0;
			if (Flags.HasFlag(ServerConfigFlags.Flags))        InstanceFlags         = (RoomFlags)buffer.ReadUInt();
			if (Flags.HasFlag(ServerConfigFlags.MinTPS))       MinTps                = buffer.ReadByte();
			if (Flags.HasFlag(ServerConfigFlags.MaxTPS))       MaxTps                = buffer.ReadByte();
			if (Flags.HasFlag(ServerConfigFlags.LoadBalancing)) LoadBalancingEnabled = buffer.ReadByte() != 0;

			return true;
		}

		public override string ToString()
			=> $"{GetType().Name}[Result={Result}, Flags={Flags}, Tps={Tps}]";
	}
}
