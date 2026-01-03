using System;
using System.Collections.Generic;
using Nox.CCK.Network;
using Nox.CCK.Utils;
using Nox.Relay.Core.Rooms;
using Nox.Relay.Core.Types.Content.Rooms;
using Nox.Worlds;
using Buffer = Nox.CCK.Utils.Buffer;
using CCKWorldIdentifier = Nox.CCK.Worlds.WorldIdentifier;

namespace Nox.Relay.Core.Types.Traveling {
    /// <summary>
    /// Event sent when a player is traveling to another dimension.
    /// </summary>
    public class TravelingEvent : RoomResponse {
        /// <summary>
        /// The result of the traveling attempt.
        /// </summary>
        public TravelingResults Results;

        /// <summary>
        /// Indicates whether the traveling event is ready to enter the dimension.
        /// </summary>
        public bool IsReady
            => Results.HasFlag(TravelingResults.Ready);

        /// <summary>
        /// Indicates whether the traveling event requires downloading the dimension from a URL.
        /// </summary>
        public bool UseUrl
            => Results.HasFlag(TravelingResults.UseUrl);

        /// <summary>
        /// Indicates whether the traveling event requires connecting to a master server node.
        /// </summary>
        public bool UseNode
            => Results.HasFlag(TravelingResults.UseNode);

        /// <summary>
        /// Indicates whether the traveling event requires using a hash to identify the dimension.
        /// </summary>
        public bool UseHash
            => Results.HasFlag(TravelingResults.UseHash);

        /// <summary>
        /// Indicates whether the traveling event requires a password to load the dimension.
        /// </summary>
        public bool HasPassword
            => Results.HasFlag(TravelingResults.HasPassword);

        /// <summary>
        /// Indicates whether the traveling event was successful.
        /// </summary>
        public bool IsSuccess
            => IsReady || UseUrl || UseNode;

        /// <summary>
        /// The reason for failure, if applicable.
        /// It can be null/empty.
        /// </summary>
        public string Reason;

        /// <summary>
        /// The URL to download the dimension from, if applicable.
        /// <remarks>Is filled only if <see cref="UseUrl"/> is true.</remarks>
        /// </summary>
        public string DownloadUrl;

        /// <summary>
        /// The hash of the dimension data, if applicable.
        /// <remarks>Is filled only if <see cref="UseUrl"/> or <see cref="UseHash"/> is true.</remarks>
        /// </summary>
        public byte[] Hash;

        /// <summary>
        /// The size of the dimension data in bytes, if applicable.
        /// <remarks>Is filled only if <see cref="UseUrl"/> is true.</remarks>
        /// </summary>
        public uint Size;

        /// <summary>
        /// The world identifier for the dimension to load, if applicable.
        /// <remarks>Is filled only if <see cref="UseNode"/> is true.</remarks>
        /// Other information like hash, version, password are stored inside the identifier's metadata.
        /// </summary>
        public IWorldIdentifier WorldIdentifier;

        /// <summary>
        /// The password to load the dimension, if applicable.
        /// <remarks>Is filled only if <see cref="HasPassword"/> is true.</remarks>
        /// </summary>
        public byte[] Password;

        /// <summary>
        /// Creates an unknown traveling event with the given reason.
        /// </summary>
        /// <param name="room"></param>
        /// <param name="reason"></param>
        /// <returns></returns>
        public static TravelingEvent Unknown(Room room, string reason)
            => new() {
                Connection = room.Connection,
                Room = room,
                Results = TravelingResults.Unknown,
                Reason = reason
            };

        public override bool FromBuffer(Buffer buffer) {
            buffer.Start();

            Results = buffer.ReadEnum<TravelingResults>();

            if (Results.HasFlag(TravelingResults.Unknown)) {
                if (buffer.Remaining > 2)
                    Reason = buffer.ReadString();
                return true;
            }

            if (UseUrl) {
                DownloadUrl = buffer.ReadString();

                Hash = buffer.ReadBytes(32);
                Size = buffer.ReadUInt();

                Password = HasPassword
                    ? buffer.ReadBytes(buffer.ReadUShort())
                    : Array.Empty<byte>();
            }
            else if (UseNode) {
                var meta = new Dictionary<string, string[]>();
                WorldIdentifier = new CCKWorldIdentifier(buffer.ReadUInt(), meta, buffer.ReadString());

                var version = buffer.ReadUShort();
                if (version != CCKWorldIdentifier.DefaultVersion)
                    meta.Add(CCKWorldIdentifier.VersionKey, new[] { version.ToString() });

                if (UseHash) {
                    Hash = buffer.ReadBytes(32);
                    meta.Add(CCKWorldIdentifier.HashKey, new[] { Hash.ToBase64() });
                }

                if (HasPassword) {
                    Password = buffer.ReadBytes(buffer.ReadUShort());
                    meta.Add(CCKWorldIdentifier.PasswordKey, new[] { Password.ToBase64() });
                }

                WorldIdentifier = new CCKWorldIdentifier(WorldIdentifier.Id, meta, WorldIdentifier.Server);
            }
            else if (IsReady) {
                // nothing more to read
            }
            else if (UseHash) {
                Logger.LogWarning(
                    $"A risky loading was requested ({nameof(TravelingResults.UseHash)}), which is unsupported.");
                Hash = buffer.ReadBytes(32);

                Password = HasPassword
                    ? buffer.ReadBytes(buffer.ReadUShort())
                    : Array.Empty<byte>();
            }
            else
                return false;

            return true;
        }

        public override string ToString()
            => $"{GetType().Name}[Results={Results}"
               + (!IsSuccess ? $", Message=\"{Reason}\"" : "")
               + (UseUrl ? $", Url=\"{DownloadUrl}\", Hash=\"{Hash}\", Size={Size}" : "")
               + (UseNode ? $", World={WorldIdentifier.ToString()}" : "")
               + "]";
    }
}