using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using Nox.CCK.Network;
using Nox.CCK.Sessions;
using Nox.CCK.Utils;
using Nox.Relay.Core.Connectors;
using Nox.Relay.Core.Types.Authentication;
using Nox.Relay.Core.Types.Enter;
using Nox.Relay.Core.Types.Traveling;
using Nox.Sessions;

namespace Nox.Relay.Runtime {
    public static class Helper {
        public const string IdFormat = "relay_{0}";

        public static ISession Create(Options options) {
            var session = new Session(string.Format(IdFormat, System.Guid.NewGuid()));
            session.UpdateState(Status.Pending, "Preparing...", 0f);
            session.SetTitle(options.Title);
            session.SetThumbnail(options.Thumbnail);
            session.SetDisposeOnChange(options.DisposeOnChange);
            session.SetInstance(options.InstanceIdentifier);
            session.SetWorld(options.WorldIdentifier);
            session.SetProperty("connections".Hash(), options.Connections);
            session.SetProperty("change_current".Hash(), options.ChangeCurrent);
            session.Connect(true).Forget();
            session.OnStateChanged.AddListener(e=>Logger.LogDebug($"Session {session.Id} state changed: {e.Status} - {e.Message} ({e.Progress:P1})"));
            return session;
        }

        private static async UniTask Connect(this Session session, bool doE = false) {
            var world = session.GetWorld();
            var instance = session.GetInstance();

            if (world is { IsValid: true }) {
                session.UpdateState(Status.Pending, "Fetching world data...", 0.05f);
                var asset = (await Main.WorldAPI.SearchAssets(
                        world.ToString(),
                        Main.WorldAPI.MakeAssetSearchRequest()
                            .SetEngines(new[] { EngineExtensions.CurrentEngine.GetEngineName() })
                            .SetPlatforms(new[] { PlatformExtensions.CurrentPlatform.GetPlatformName() })
                            .SetVersions(new[] { world.Version })
                            .SetLimit(1)
                    )).GetAssets()
                    .FirstOrDefault();

                if (asset == null) {
                    Logger.LogError($"Failed to find asset for world {world} with version {world.Version}");
                    session.UpdateState(Status.Error, $"World '{world}' not found", 1f);
                    return;
                }

                session.UpdateState(Status.Pending, $"Preparing world '{world}'...", 0.1f);

                if (!Main.WorldAPI.HasSceneInCache(asset.GetHash())) {
                    session.UpdateState(Status.Pending, $"Downloading world '{world}'...", 0.15f);
                    var download = Main.WorldAPI.DownloadSceneToCache(
                        asset.GetUrl(),
                        hash: asset.GetHash(),
                        progress: arg0 => session.UpdateState(Status.Pending, $"Downloading world '{world}'...",
                            0.15f + arg0 * 0.45f)
                    );
                    await download.Start();
                }

                if (!Main.WorldAPI.HasSceneInCache(asset.GetHash())) {
                    Logger.LogError($"Failed to download asset for world {world} with version {world.Version}");
                    session.UpdateState(Status.Error, $"Failed to download world '{world}'", 1f);
                    return;
                }
            }

            session.UpdateState(Status.Pending, "Connecting to relay server...", 0.6f);
            var token = await Main.UserAPI.GetToken(instance.GetServerAddress());

            if (token == null) {
                session.UpdateState(Status.Error, "Failed to fetch token", -1f);
                Logger.LogError($"Failed to fetch token for server {instance.GetServerAddress()}");
                if (doE) await session.Dispose();
                return;
            }

            IConnector con = null;
            var connections = session.GetProperty<string[]>("connections".Hash())
                              ?? Array.Empty<string>();

            foreach (var addr in connections) {
                if (con != null)
                    await con.Close();

                session.UpdateState(Status.Pending, $"Connecting to {addr}...", 0.1f);
                var (proto, endPoint) = await ConnectorHelper.ParseIPEndPoint(addr);

                con = ConnectorHelper.From(proto);
                if (con == null) {
                    Logger.LogWarning($"No connection for protocol {proto} found, trying to create a new one");
                    continue;
                }

                if (!await con.Connect(endPoint.Address.ToString(), (ushort)endPoint.Port)) {
                    Logger.LogWarning($"Failed to connect to {addr}");
                    continue;
                }

                break;
            }

            if (con == null) {
                session.UpdateState(Status.Error, "Failed to connect to any relay", -1f);
                Logger.LogError("Failed to connect to any relay");
                if (doE) await session.Dispose();
                return;
            }

            var adapter = session.Adapter;
            if (adapter != null)
                await adapter.Dispose();
            
            Logger.LogDebug($"Using connector {con.Protocol} for session {session.Id}...");
            adapter = new Core.Relay(con);
            session.SetAdapter(adapter);
            
            session.UpdateState(Status.Pending, "Handshaking...", 0.2f);
            var handshake = await adapter.Handshake();
            if (handshake is not { IsValid: true }) {
                session.UpdateState(Status.Error, "Handshake failed", 1f);
                Logger.LogError("Handshake with relay server failed");
                if (doE) await session.Dispose();
                return;
            }


            session.UpdateState(Status.Pending, "Authenticating...", 0.225f);
            var request = AuthenticationRequest.Request();
            var auth = await adapter.Authenticate(request);
            if (auth.IsError) {
                session.UpdateState(Status.Error, $"Authentication failed: {auth.Reason}", -1f);
                Logger.LogError($"Authentication failed: {auth.Result} - {auth.Reason}");
                if (doE) await session.Dispose();
                return;
            }

            var challenge = auth.Challenge;
            Logger.LogDebug($"Received challenge: {challenge.Length:X4}/{string.Join(":", challenge.Select(c => c.ToString("X2")))}");

            var keys = Crypto.GetKeys();
            var sign = Crypto.Sign(challenge, keys);

            var user = Main.UserAPI.GetCurrent();

            request = AuthenticationRequest.Resolve(
                Crypto.ExportPublicKeyToDer(keys),
                sign,
                user.ToIdentifier()
            );

            auth = await adapter.Authenticate(request);
            if (auth.IsError) {
                session.UpdateState(Status.Error, $"Authentication failed: {auth.Reason}", -1f);
                Logger.LogError($"Authentication failed: {auth.Result} - {auth.Reason}");
                if (doE) await session.Dispose();
                return;
            }

            session.UpdateState(Status.Pending, "Fetching room...", 0.25f);
            var room = await adapter.List(instance.GetId());
            if (room == null) {
                session.UpdateState(Status.Error, $"Failed to get room {instance}", -1f);
                Logger.LogError($"Failed to get room {instance}");
                if (doE) await session.Dispose();
                return;
            }

            room.OnQuited.AddListener(session.OnPlayerQuitedHandler);
            room.OnJoined.AddListener(session.OnPlayerJoinedHandler);
            room.OnLeft.AddListener(session.OnPlayerLeftHandler);
            room.OnTransform.AddListener(session.OnTransformHandler);
            room.OnProperties.AddListener(session.OnPropertiesHandler);
            room.OnEvent.AddListener(session.OnEventHandler);
            // adapter.Instance.OnAvatarChanged.AddListener(adapter.OnAvatarChanged);
            // adapter.Instance.OnPlayerUpdated.AddListener(adapter.OnPlayerUpdated);


            session.UpdateState(Status.Pending, "Entering room...", 0.3f);
            var enter = await room.Enter(new EnterRequest());
            if (enter.IsError) {
                session.UpdateState(Status.Error, $"Failed to connect to room: {enter.Result} - {enter.Reason}", -1f);
                Logger.LogError($"Failed to connect to room {instance}: {enter.Result} - {enter.Reason}");
                if (doE) await session.Dispose();
                return;
            }

            room.Tps = enter.Tps;
            room.Threshold = enter.Threshold;
            room.RenderEntity = enter.RenderEntity;

            session.UpdateState(Status.Pending, "Traveling to room...", 0.325f);

            var travelInfos = await room.Traveling(TravelingRequest.Travel());
            if (!travelInfos.IsSuccess) {
                session.UpdateState(Status.Error, $"Failed to travel to room: {travelInfos.Reason}", -1f);
                Logger.LogError($"Failed to travel to room {instance}: {travelInfos.Results} - {travelInfos.Reason}");
                if (doE) await session.Dispose();
                return;
            }

            var traveling = await session.OnTravelingAsync(
                travelInfos,
                response: false,
                progress: (f, s) => session.UpdateState(Status.Pending, s, 0.325f + f * 0.575f)
            );

            if (!traveling) {
                session.UpdateState(Status.Pending, "Failed to travel to room", -1f);
                Logger.LogError($"Failed to travel to room {instance}");
                await session.Dispose();
                return;
            }

            session.UpdateState(Status.Pending, $"Making ready in instance {instance}...", 0.9f);

            var travelReady = await room.Traveling(TravelingRequest.Ready());
            if (!travelReady.IsReady) {
                session.UpdateState(Status.Error, $"Failed to travel to room {travelInfos.Reason}", -1f);
                Logger.LogError($"Failed to travel to room {instance}: {travelReady.Results} - {travelReady.Reason}");
                if (doE) await session.Dispose();
                return;
            }

            room.OnTraveling.AddListener(session.OnTraveling);
            room.OnEntered.AddListener(session.OnPlayerEnteredHandler);

            Logger.LogDebug($"Local player: {enter.Player.Display} ({enter.Player.Id}, {enter.Player.Flags})");
            session.OnPlayerEnteredHandler(enter, false);

            if (session.TryGetProperty<bool>("change_current".Hash(), out var current) && current) {
                session.UpdateState(Status.Pending, "Setting room as current...", 0.95f);
                await Main.SessionAPI.SetCurrent(session.Id);
            }

            session.UpdateState(Status.Ready,  "Ready", 1f);
        }
    }
}