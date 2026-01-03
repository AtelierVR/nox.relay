using System.Net;
using Nox.CCK.Utils;
using Nox.Relay.Core.Types.Contents;

namespace Nox.Relay.Core.Types.Handshakes {
    /// <summary>
    /// Response sent by the server upon a successful handshake initiation by the client.
    /// </summary>
    public class HandshakeResponse : ContentResponse {
        /// <summary>
        /// Protocol version used by the server.
        /// </summary>
        public ushort Protocol;

        /// <summary>
        /// Unique identifier assigned to the client by the server.
        /// </summary>
        public ushort ClientId;

        /// <summary>
        /// Is your external IP and port as seen by the server.
        /// </summary>
        public IPEndPoint Address;

        /// <summary>
        /// Flags providing additional information about the server.
        /// </summary>
        public HandshakeFlags Flags;

        /// <summary>
        /// Address of the node server of the relay, if applicable.
        /// </summary>
        public string MasterAddress;

        /// <summary>
        /// Maximum packet size supported by the server.
        /// Is used to optimize data transmission and segmentation.
        /// </summary>
        public ushort MaxPacketSize;

        /// <summary>
        /// Connection timeout duration in milliseconds.
        /// Specifies how long the server will wait for client activity before considering the connection lost.
        /// </summary>
        public ushort ConnectionTimeout;

        /// <summary>
        /// Keep-alive interval in milliseconds.
        /// Defines how often the client should send keep-alive messages to maintain the connection.
        /// </summary>
        public ushort KeepAliveInterval;

        /// <summary>
        /// Segmentation timeout in milliseconds.
        /// Indicates the maximum time to wait for all segments of a fragmented packet before discarding it.
        /// </summary>
        public ushort SegmentationTimeout;

        /// <summary>
        /// Validates the handshake response to ensure all required fields are correctly set.
        /// </summary>
        public bool IsValid
            => Protocol == Relay.ProtocolVersion;

        public override bool FromBuffer(Buffer buffer) {
            buffer.Start();
            
            Logger.LogDebug($"Starting {nameof(HandshakeResponse)} {buffer}");

            Protocol = buffer.ReadUShort();
            ClientId = buffer.ReadUShort();

            // Read external address
            var address = buffer.ReadBytes(4);
            var port = buffer.ReadUShort();
            Address = new IPEndPoint(new IPAddress(address), port);

            Flags = buffer.ReadEnum<HandshakeFlags>();

            // Read MasterAddress only if not offline
            MasterAddress = !Flags.HasFlag(HandshakeFlags.IsOffline)
                ? buffer.ReadString()
                : null;

            // Read additional server configuration
            MaxPacketSize = buffer.ReadUShort();
            ConnectionTimeout = buffer.ReadUShort();
            KeepAliveInterval = buffer.ReadUShort();
            SegmentationTimeout = buffer.ReadUShort();

            return true;
        }

        public override string ToString()
            =>
                $"{GetType().Name}[Protocol={Protocol}, ClientId={ClientId}, Address={Address}, MaxPacketSize={MaxPacketSize}]";
    }
}