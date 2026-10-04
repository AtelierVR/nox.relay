using System;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Nox.CCK.Network;
using Nox.CCK.Sessions;
using Nox.CCK.Utils;
using Nox.CCK.Network.Assets;
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
			session.UpdateState(Status.Pending, "Preparing...", 0f, cancelable: true);
			session.SetTitle(options.Title);
			session.SetShortName(options.ShortName);
			session.SetThumbnail(options.Thumbnail);
			session.SetDisposeOnChange(options.DisposeOnChange);
			session.SetInstance(options.InstanceIdentifier);
			session.SetWorld(options.WorldIdentifier);
			session.SetProperty("connections".Hash(), options.Connections);
			session.SetProperty("change_current".Hash(), options.ChangeCurrent);
			session.ConnectCts = new CancellationTokenSource();
			session.Connect(true, session.ConnectCts.Token).Forget();
			session.OnStateChanged.AddListener(e => Logger.LogDebug($"State changed: {e.Status} - {e.Message} ({e.Progress:P1})", session.Tag));
			return session;
		}

		/// <summary>
		/// Wrapper around <see cref="ConnectInternal"/>.
		/// The connect task is started with <c>Forget()</c>, so any unhandled exception would
		/// surface as an UnobservedTaskException (logged by UniTaskScheduler) and would leave the
		/// session stuck in the "Pending" state forever. Everything is caught here so the session
		/// is always driven to a terminal state (Error/Ready) instead.
		/// </summary>
		private static async UniTask Connect(this Session session, bool doE, CancellationToken token) {
			try {
				await ConnectInternal(session, doE, token);
			} catch (OperationCanceledException) {
				Logger.LogDebug("Connection cancelled", session.Tag);
				try {
					await session.Dispose();
				} catch (Exception disposeError) {
					Logger.LogException(disposeError, session.Tag);
				}
			} catch (Exception e) {
				Logger.LogException(e, session.Tag);
				try {
					session.UpdateState(Status.Error, $"Connection failed: {e.Message}", -1f);
				} catch (Exception stateError) {
					// The session (or its UI) may already be disposed, never let this bubble up.
					Logger.LogException(stateError, session.Tag);
				}

				if (doE) {
					try {
						await session.Dispose();
					} catch (Exception disposeError) {
						Logger.LogException(disposeError, session.Tag);
					}
				}
			}
		}

		private static async UniTask ConnectInternal(Session session, bool doE, CancellationToken token) {
			token.ThrowIfCancellationRequested();

			var world    = session.GetWorld();
			var instance = session.GetInstance();

			if (world.IsValid()) {
				session.UpdateState(Status.Pending, "Fetching world data...", 0.05f);

				var asset = await Main.WorldAPI.ResolveBundle(world, token);

				var hash = asset?.CacheKey();

				if (asset == null || string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(asset.Url)) {
					Logger.LogError($"Failed to find a compatible bundle for world {world}", session.Tag);
					session.UpdateState(Status.Error, $"World '{world}' not found", 1f);
					return;
				}

				session.UpdateState(Status.Pending, $"Preparing world '{world}'...", 0.1f);

				if (!Main.WorldAPI.HasInCache(hash)) {
					session.UpdateState(Status.Pending, $"Downloading world '{world}'...", 0.15f);
					var download = Main.WorldAPI.DownloadToCache(
						asset.Url,
						hash: hash,
						progress: arg0 => session.UpdateState(Status.Pending, $"Downloading world '{world}'...",
							0.15f + arg0 * 0.45f),
						token: token
					);
					await download.Start();
				}

				if (!Main.WorldAPI.HasInCache(hash)) {
					Logger.LogError($"Failed to download the bundle of world {world}", session.Tag);
					session.UpdateState(Status.Error, $"Failed to download world '{world}'", 1f);
					return;
				}
			}

			// The relay authenticates via challenge-response (see auth.rs).
			// No bearer token is required or sent.
			session.UpdateState(Status.Pending, "Connecting to relay server...", 0.6f, cancelable: true);

			IConnector con = null;
			var connections = session.GetProperty<string[]>("connections".Hash())
				?? Array.Empty<string>();

			foreach (var addr in connections) {
				session.UpdateState(Status.Pending, $"Connecting to {addr}...", 0.1f, cancelable: true);
				var (proto, host, endPoint) = await ConnectorHelper.ParseIPEndPoint(addr, token);

				con = ConnectorHelper.From(proto);
				if (con == null) {
					Logger.LogWarning($"No connection for protocol {proto} found, trying to create a new one", session.Tag);
					con = null;
					continue;
				}

				if (!await con.Connect(host, (ushort)endPoint.Port, token)) {
					Logger.LogWarning($"Failed to connect to {addr}", session.Tag);
					con = null;
					continue;
				}

				break;
			}

			if (con == null) {
				session.UpdateState(Status.Error, "Failed to connect to any relay", -1f);
				Logger.LogError("Failed to connect to any relay", session.Tag);
				if (doE)
					await session.Dispose();
				return;
			}

			var adapter = session.Adapter;
			if (adapter != null)
				await adapter.Dispose();

			Logger.LogDebug($"Using connector {con.Protocol} for session {session.Id}...", session.Tag);
			adapter = new Core.Relay(con);
			session.SetAdapter(adapter);

			session.UpdateState(Status.Pending, "Handshaking...", 0.2f, cancelable: true);
			await UniTask.SwitchToMainThread();

			var handshake = await adapter.Handshake(token);
			if (handshake is not { IsValid: true }) {
				session.UpdateState(Status.Error, "Handshake failed", 1f);
				Logger.LogError("Handshake with relay server failed", session.Tag);
				if (doE)
					await session.Dispose();
				return;
			}


			session.UpdateState(Status.Pending, "Authenticating...", 0.225f, cancelable: true);
			var request = AuthenticationRequest.Request();
			var auth    = await adapter.Authenticate(request, token);
			if (auth.IsError) {
				session.UpdateState(Status.Error, $"Authentication failed: {auth.Reason}", -1f);
				Logger.LogError($"Authentication failed: {auth.Result} - {auth.Reason}", session.Tag);
				if (doE)
					await session.Dispose();
				return;
			}

			var challenge = auth.Challenge;
			Logger.LogDebug($"Received challenge: {challenge.Length:X4}/{string.Join(":", challenge.Select(c => c.ToString("X2")))}", session.Tag);

			var keys = Crypto.GetKeys();
			var sign = Crypto.Sign(challenge, keys);

			// The current user can legitimately be null: no server configured, "/users/@me" fetch
			// failed (offline, TLS/DNS error, expired or missing token) or the user logged out
			// while the session was being created. Bail out cleanly instead of dereferencing it.
			var user = Main.UserAPI?.Current;
			if (user == null) {
				session.UpdateState(Status.Error, "Not signed in", -1f);
				Logger.LogError(
					"Failed to authenticate to relay: no current user (not signed in, token expired or /users/@me fetch failed)",
					session.Tag);
				if (doE)
					await session.Dispose();
				return;
			}

			request = AuthenticationRequest.Resolve(
				Crypto.ExportPublicKeyToDer(keys),
				sign,
				user.Identifier
			);

			auth = await adapter.Authenticate(request, token);
			if (auth.IsError) {
				session.UpdateState(Status.Error, $"Authentication failed: {auth.Reason}", -1f);
				Logger.LogError($"Authentication failed: {auth.Result} - {auth.Reason}", session.Tag);
				if (doE)
					await session.Dispose();
				return;
			}

			session.UpdateState(Status.Pending, "Fetching room...", 0.25f, cancelable: true);
			var room = await adapter.List(instance.NumericId, token);
			if (room == null) {
				session.UpdateState(Status.Error, $"Failed to get room {instance}", -1f);
				Logger.LogError($"Failed to get room {instance}", session.Tag);
				if (doE)
					await session.Dispose();
				return;
			}

			room.OnQuited.AddListener(session.OnPlayerQuitedHandler);
			room.OnJoined.AddListener(session.OnPlayerJoinedHandler);
			room.OnLeft.AddListener(session.OnPlayerLeftHandler);
			room.OnTransform.AddListener(session.OnTransformHandler);
			room.OnProperties.AddListener(session.OnPropertiesHandler);
			room.OnEvent.AddListener(session.OnEventHandler);
			room.OnAvatarChanged.AddListener(session.OnAvatarChanged);
			// adapter.Instance.OnPlayerUpdated.AddListener(adapter.OnPlayerUpdated);

			session.UpdateState(Status.Pending, "Entering room...", 0.3f, cancelable: true);
			var enter = await room.Enter(new EnterRequest(), token);
			if (enter.IsError) {
				session.UpdateState(Status.Error, $"Failed to connect to room: {enter.Result} - {enter.Reason}", -1f);
				Logger.LogError($"Failed to connect to room {instance}: {enter.Result} - {enter.Reason}", session.Tag);
				if (doE)
					await session.Dispose();
				return;
			}

			room.Tps          = enter.Tps;
			room.Threshold    = enter.Threshold;
			room.RenderEntity = enter.RenderEntity;

			session.UpdateState(Status.Pending, "Traveling to room...", 0.325f, cancelable: true);

			var travelInfos = await room.Traveling(TravelingRequest.Travel(), token);
			if (!travelInfos.IsSuccess) {
				session.UpdateState(Status.Error, $"Failed to travel to room: {travelInfos.Reason}", -1f);
				Logger.LogError($"Failed to travel to room {instance}: {travelInfos.Results} - {travelInfos.Reason}", session.Tag);
				if (doE)
					await session.Dispose();
				return;
			}

			var traveling = await session.OnTravelingAsync(
				travelInfos,
				response: false,
				progress: (f, s) => session.UpdateState(Status.Pending, s, 0.325f + f * 0.575f),
				token: token
			);

			if (!traveling) {
				session.UpdateState(Status.Pending, "Failed to travel to room", -1f);
				Logger.LogError($"Failed to travel to room {instance}", session.Tag);
				await session.Dispose();
				return;
			}

			session.UpdateState(Status.Pending, $"Making ready in instance {instance}...", 0.9f);

			var travelReady = await room.Traveling(TravelingRequest.Ready(), token);
			if (!travelReady.IsReady) {
				session.UpdateState(Status.Error, $"Failed to travel to room {travelInfos.Reason}", -1f);
				Logger.LogError($"Failed to travel to room {instance}: {travelReady.Results} - {travelReady.Reason}", session.Tag);
				if (doE)
					await session.Dispose();
				return;
			}

			room.OnTraveling.AddListener(session.OnTraveling);
			room.OnEntered.AddListener(session.OnPlayerEnteredHandler);

			Logger.LogDebug($"Local player: {enter.Player.Display} ({enter.Player.Id}, {enter.Player.Flags})", session.Tag);
			session.OnPlayerEnteredHandler(enter, false);

			if (session.TryGetProperty<bool>("change_current".Hash(), out var current) && current) {
				session.UpdateState(Status.Pending, "Setting room as current...", 0.95f);
				await Main.SessionAPI.SetCurrent(session.Id, token);
			}

			session.UpdateState(Status.Ready, "Ready", 1f);
		}
	}
}