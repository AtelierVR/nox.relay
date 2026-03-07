using Nox.CCK.Utils;
using Nox.Relay.Core.Types.Contents.Rooms;
using Nox.Relay.Core.Types.Rooms;
using Buffer = Nox.CCK.Utils.Buffer;

namespace Nox.Relay.Core.Types.ServerConfig {
	/// <summary>
	/// Request to read or modify the configuration of an instance.
	/// Requires <c>INSTANCE_OWNER</c> or <c>MASTER_MODERATOR</c> privileges.
	/// <para>
	/// Set <see cref="Flags"/> to <see cref="ServerConfigFlags.None"/> to receive an echo of the
	/// current configuration without making any changes.
	/// </para>
	/// Wire format (after outer <c>[iid:u8]</c> prefix added by <c>Room.Emit</c>):
	/// <code>[flags:u8][tps:u8?][threshold:f32?][capacity:u16?][password:string?][instance_flags:u32?][lb_enabled:u8?]</code>
	/// </summary>
	public class ServerConfigRequest : RoomRequest {
		/// <summary>Which fields to update. Use <see cref="ServerConfigFlags.None"/> for a read-only echo.</summary>
		public ServerConfigFlags Flags = ServerConfigFlags.None;

		/// <summary>New TPS value. Required when <see cref="Flags"/> includes <see cref="ServerConfigFlags.TPS"/>.</summary>
		public byte Tps;

		/// <summary>New transform threshold. Required when <see cref="Flags"/> includes <see cref="ServerConfigFlags.Threshold"/>.</summary>
		public float Threshold;

		/// <summary>New player capacity. Required when <see cref="Flags"/> includes <see cref="ServerConfigFlags.Capacity"/>.</summary>
		public ushort Capacity;

		/// <summary>New password (empty string to clear). Required when <see cref="Flags"/> includes <see cref="ServerConfigFlags.Password"/>.</summary>
		public string Password;

		/// <summary>New instance flags. Required when <see cref="Flags"/> includes <see cref="ServerConfigFlags.Flags"/>.</summary>
		public RoomFlags InstanceFlags;

		/// <summary>Enable or disable load balancing. Required when <see cref="Flags"/> includes <see cref="ServerConfigFlags.LoadBalancing"/>.</summary>
		public bool LoadBalancingEnabled;

		public override Buffer ToBuffer() {
			var buffer = new Buffer();
			buffer.Write((byte)Flags);

			if (Flags.HasFlag(ServerConfigFlags.TPS))          buffer.Write(Tps);
			if (Flags.HasFlag(ServerConfigFlags.Threshold))    buffer.Write(Threshold);
			if (Flags.HasFlag(ServerConfigFlags.Capacity))     buffer.Write(Capacity);
			if (Flags.HasFlag(ServerConfigFlags.Password))     buffer.Write(Password ?? string.Empty);
			if (Flags.HasFlag(ServerConfigFlags.Flags))        buffer.Write((uint)InstanceFlags);
			if (Flags.HasFlag(ServerConfigFlags.LoadBalancing)) buffer.Write((byte)(LoadBalancingEnabled ? 1 : 0));

			return buffer;
		}
	}
}
