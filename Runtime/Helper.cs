using System;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Nox.CCK.Network;
using Nox.CCK.Sessions;
using Nox.CCK.Utils;
using Nox.CCK.Network.Assets;
using Nox.Relay.Core.Connectors;
using Nox.Relay.Core.Rooms;
using Nox.Relay.Core.Types.Authentication;
using Nox.Relay.Core.Types.Enter;
using Nox.Relay.Core.Types.Handshakes;
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
			session.SetProperty("display".Hash(), options.Display);
			session.SetProperty("password".Hash(), options.Password);
			session.AutoReconnect        = options.Reconnect;
			session.ReconnectMaxAttempts = options.ReconnectAttempts;
			session.ReconnectInitialDelay = options.ReconnectDelay;
			session.ReconnectMaxDelay    = options.ReconnectMaxDelay;
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
			var (adapter, offline) = await ConnectAdapter(session, token);
			if (adapter == null) {
				if (doE)
					await session.Dispose();
				return;
			}

			session.UpdateState(Status.Pending, "Fetching room...", 0.25f, cancelable: true);
			var room = await ResolveRoom(session, adapter, null, token);
			if (room == null) {
				session.UpdateState(Status.Error, $"Failed to get room {instance}", -1f);
				Logger.LogError($"Failed to get room {instance}", session.Tag);
				if (doE)
					await session.Dispose();
				return;
			}

			HookRoomEvents(session, room);

			session.UpdateState(Status.Pending, "Entering room...", 0.3f, cancelable: true);
			// Optional in-room pseudonym and instance password (see the `relay connect` flags).
			var enter = await room.Enter(new EnterRequest {
				Display  = session.GetProperty<string>("display".Hash())
					?? (offline ? Main.UserAPI?.Current?.Display : null),
				Password = session.GetProperty<string>("password".Hash()),
			}, token);
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

			// The relay tells us where to travel: a master node (UseNode) or a bundle URL
			// (UseBundle, e.g. a standalone relay with a configured world) — both are handled.
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

		/// <summary>
		/// Connects a connector to the first reachable relay address of the session.
		/// Returns <c>null</c> when every address failed (failed connectors are disposed here, which
		/// matters for QUIC: a registration left alive crashes the process when the GC finalizes it).
		/// </summary>
		private static async UniTask<IConnector> ConnectToAnyRelay(Session session, CancellationToken token, string addressOverride = null) {
			session.UpdateState(Status.Pending, "Connecting to relay server...", 0.6f, cancelable: true);

			IConnector con = null;
			// A transfer names one explicit destination, every other connection uses the session list.
			var connections = string.IsNullOrEmpty(addressOverride)
				? session.GetProperty<string[]>("connections".Hash()) ?? Array.Empty<string>()
				: new[] { addressOverride };

			foreach (var addr in connections) {
				token.ThrowIfCancellationRequested();

				session.UpdateState(Status.Pending, $"Connecting to {addr}...", 0.1f, cancelable: true);
				var (proto, host, endPoint) = await ConnectorHelper.ParseIPEndPoint(addr, token);

				// ParseIPEndPoint returns nulls when the address is malformed (or the host cannot be
				// resolved) — skip it instead of dereferencing a null endpoint below.
				if (proto == null || host == null || endPoint == null) {
					Logger.LogWarning($"Invalid relay address '{addr}', skipping", session.Tag);
					continue;
				}

				con = ConnectorHelper.From(proto);
				if (con == null) {
					Logger.LogWarning($"No connection for protocol {proto} found, trying to create a new one", session.Tag);
					continue;
				}

				if (await con.Connect(host, (ushort)endPoint.Port, token))
					return con;

				Logger.LogWarning($"Failed to connect to {addr}", session.Tag);

				// Never leave a half-initialized connector behind (QUIC keeps native resources).
				try {
					await con.Dispose();
				} catch (Exception e) {
					Logger.LogException(e, session.Tag);
				}

				con = null;
			}

			return con;
		}

		/// <summary>
		/// Resolves the room to enter: an explicit room when one is given (room move), otherwise the
		/// session's own one. A relay that advertises a default room (standalone relay hosting a
		/// single one, or a public hub) is used as a fallback when the requested room does not exist
		/// there.
		/// </summary>
		private static async UniTask<Room> ResolveRoom(
			Session session,
			Core.Relay adapter,
			byte? roomId,
			CancellationToken token
		) {
			if (roomId is byte iid)
				return await adapter.Find(iid, token);

			var instance = session.GetInstance();
			var room = await adapter.List(instance.NumericId, token);
			if (room != null)
				return room;

			var auto = adapter.LastHandshake?.AutoRoom ?? HandshakeResponse.NoAutoRoom;
			if (auto == HandshakeResponse.NoAutoRoom)
				return null;

			Logger.LogDebug(
				$"Room {instance} not found, using the relay default room {auto}",
				session.Tag);
			return await adapter.Find(auto, token);
		}

		/// <summary>
		/// Connects a new connector and performs the handshake/authentication exchange.
		/// Returns the ready adapter and whether the relay is offline (standalone), or
		/// <c>(null, false)</c> on failure. The previous adapter of the session is disposed.
		/// </summary>
		private static async UniTask<(Core.Relay adapter, bool offline)> ConnectAdapter(
			Session session,
			CancellationToken token,
			string addressOverride = null,
			byte[] transferToken = null
		) {
			// Detach the previous adapter first: its shutdown must not raise the disconnected handler,
			// and native resources have to be released even if the new connection fails.
			await session.DetachAdapterAsync();

			var con = await ConnectToAnyRelay(session, token, addressOverride);
			if (con == null) {
				session.UpdateState(Status.Error, "Failed to connect to any relay", -1f);
				Logger.LogError("Failed to connect to any relay", session.Tag);
				return (null, false);
			}

			// The session may have been disposed while the connector was connecting.
			if (session.IsDisposed || token.IsCancellationRequested) {
				try {
					await con.Dispose();
				} catch (Exception e) {
					Logger.LogException(e, session.Tag);
				}

				token.ThrowIfCancellationRequested();
				return (null, false);
			}

			Logger.LogDebug($"Using connector {con.Protocol} for session {session.Id}...", session.Tag);
			var adapter = new Core.Relay(con);
			session.SetAdapter(adapter);

			session.UpdateState(Status.Pending, "Handshaking...", 0.2f, cancelable: true);
			await UniTask.SwitchToMainThread();

			var handshake = await adapter.Handshake(token, transferToken);
			if (handshake is not { IsValid: true }) {
				session.UpdateState(Status.Error, "Handshake failed", 1f);
				Logger.LogError("Handshake with relay server failed", session.Tag);
				await session.DetachAdapterAsync();
				return (null, false);
			}

			// The server tells us whether it is backed by a master node (IsOffline when it is not,
			// e.g. a standalone relay). Adapt the rest of the connect flow to that.
			var offline = handshake.Flags.HasFlag(HandshakeFlags.IsOffline);

			// A transferred handshake already authenticated us: the relay resolved the token, so skip
			// the challenge-response (it would be rejected).
			if (handshake.IsTransferred) {
				Logger.LogDebug("Authenticated by transfer token", session.Tag);
				return (adapter, offline);
			}

			session.UpdateState(Status.Pending, "Authenticating...", 0.225f, cancelable: true);
			var request = AuthenticationRequest.Request();
			var auth    = await adapter.Authenticate(request, token);
			if (auth.IsError) {
				session.UpdateState(Status.Error, $"Authentication failed: {auth.Reason}", -1f);
				Logger.LogError($"Authentication failed: {auth.Result} - {auth.Reason}", session.Tag);
				await session.DetachAdapterAsync();
				return (null, false);
			}

			var challenge = auth.Challenge;
			Logger.LogDebug($"Received challenge: {challenge.Length:X4}/{string.Join(":", challenge.Select(c => c.ToString("X2")))}", session.Tag);

			var keys = Crypto.GetKeys();
			var sign = Crypto.Sign(challenge, keys);

			// The current user can legitimately be null: no server configured, "/users/@me" fetch
			// failed (offline, TLS/DNS error, expired or missing token) or the user logged out
			// while the session was being created. An offline relay has no node identity to check:
			// present a local one (the relay trusts the verified key).
			var user = Main.UserAPI?.Current;
			Identifier identity;
			if (user != null) {
				identity = user.Identifier;
			} else if (offline) {
				identity = Identifier.Invalid;
			} else {
				session.UpdateState(Status.Error, "Not signed in", -1f);
				Logger.LogError(
					"Failed to authenticate to relay: no current user (not signed in, token expired or /users/@me fetch failed)",
					session.Tag);
				await session.DetachAdapterAsync();
				return (null, false);
			}

			request = AuthenticationRequest.Resolve(
				Crypto.ExportPublicKeyToDer(keys),
				sign,
				identity
			);

			auth = await adapter.Authenticate(request, token);
			if (auth.IsError) {
				session.UpdateState(Status.Error, $"Authentication failed: {auth.Reason}", -1f);
				Logger.LogError($"Authentication failed: {auth.Result} - {auth.Reason}", session.Tag);
				await session.DetachAdapterAsync();
				return (null, false);
			}

			return (adapter, offline);
		}

		/// <summary>Subscribes the session to the room events of a fresh connection.</summary>
		private static void HookRoomEvents(Session session, Room room) {
			room.OnQuited.AddListener(session.OnPlayerQuitedHandler);
			room.OnJoined.AddListener(session.OnPlayerJoinedHandler);
			room.OnLeft.AddListener(session.OnPlayerLeftHandler);
			room.OnTransform.AddListener(session.OnTransformHandler);
			room.OnProperties.AddListener(session.OnPropertiesHandler);
			room.OnEvent.AddListener(session.OnEventHandler);
			room.OnAvatarChanged.AddListener(session.OnAvatarChanged);
			room.OnEntity.AddListener(session.OnEntityHandler);
		}

		/// <summary>
		/// Restores a session after an unexpected disconnection: new transport, new authentication,
		/// same room. The destination world is already loaded, so the travel is only acknowledged
		/// (never re-downloaded or reloaded).
		/// </summary>
		/// <returns><c>true</c> when the session is back in its room, <c>false</c> otherwise.</returns>
		/// <param name="addressOverride">Relay to use instead of the session's connection list (transfer).</param>
		/// <param name="transferToken">Opaque client-unique token issued elsewhere (transfer).</param>
		internal static async UniTask<bool> Reconnect(
			Session session,
			CancellationToken token,
			string addressOverride = null,
			byte[] transferToken = null
		) {
			token.ThrowIfCancellationRequested();

			var (adapter, offline) = await ConnectAdapter(session, token, addressOverride, transferToken);
			if (adapter == null)
				return false;

			return await RestoreRoom(session, adapter, offline, token);
		}

		/// <summary>
		/// Switches a connected session to another room of the same relay: the transport and the
		/// loaded world are kept, only the room membership is rebuilt. Moving to a room of another
		/// world requires a transfer (the bundle would have to be downloaded).
		/// </summary>
		internal static async UniTask<bool> Move(
			Session session,
			byte roomId,
			CancellationToken token
		) {
			token.ThrowIfCancellationRequested();

			var adapter = session.Adapter;
			if (adapter == null) {
				Logger.LogWarning("Cannot move: no relay connection", session.Tag);
				return false;
			}

			var offline = adapter.LastHandshake?.Flags.HasFlag(HandshakeFlags.IsOffline) ?? false;
			return await RestoreRoom(session, adapter, offline, token, roomId);
		}

		/// <summary>
		/// Rebuilds the room membership on a live adapter (reconnection, transfer or room move).
		/// </summary>
		/// <param name="roomId">Room to join instead of the session's own (room move).</param>
		private static async UniTask<bool> RestoreRoom(
			Session session,
			Core.Relay adapter,
			bool offline,
			CancellationToken token,
			byte? roomId = null
		) {
			var instance = session.GetInstance();

			session.UpdateState(Status.Pending, "Fetching room...", 0.4f, cancelable: true);
			var room = await ResolveRoom(session, adapter, roomId, token);
			if (room == null) {
				Logger.LogWarning($"Failed to get room {instance} while reconnecting", session.Tag);
				await session.DetachAdapterAsync();
				return false;
			}

			HookRoomEvents(session, room);

			session.UpdateState(Status.Pending, "Entering room...", 0.55f, cancelable: true);
			var enter = await room.Enter(new EnterRequest {
				Display  = session.GetProperty<string>("display".Hash())
					?? (offline ? Main.UserAPI?.Current?.Display : null),
				Password = session.GetProperty<string>("password".Hash()),
			}, token);
			if (enter.IsError) {
				Logger.LogWarning(
					$"Failed to enter room {instance} while reconnecting: {enter.Result} - {enter.Reason}",
					session.Tag);
				await session.DetachAdapterAsync();
				return false;
			}

			room.Tps          = enter.Tps;
			room.Threshold    = enter.Threshold;
			room.RenderEntity = enter.RenderEntity;

			session.UpdateState(Status.Pending, "Restoring room...", 0.7f, cancelable: true);

			// Ask the relay where the room lives, then acknowledge the travel right away: the
			// destination world is already loaded in this session.
			var travelInfos = await room.Traveling(TravelingRequest.Travel(), token);
			if (!travelInfos.IsSuccess) {
				Logger.LogWarning(
					$"Failed to travel to room {instance} while reconnecting: {travelInfos.Results} - {travelInfos.Reason}",
					session.Tag);
				await session.DetachAdapterAsync();
				return false;
			}

			var travelReady = await room.Traveling(TravelingRequest.Ready(), token);
			if (!travelReady.IsReady) {
				Logger.LogWarning(
					$"Failed to acknowledge the travel to room {instance} while reconnecting: {travelReady.Results} - {travelReady.Reason}",
					session.Tag);
				await session.DetachAdapterAsync();
				return false;
			}

			room.OnTraveling.AddListener(session.OnTraveling);
			room.OnEntered.AddListener(session.OnPlayerEnteredHandler);

			Logger.LogDebug($"Local player: {enter.Player.Display} ({enter.Player.Id}, {enter.Player.Flags})", session.Tag);
			session.OnPlayerEnteredHandler(enter, false);

			// Rebuild the relay-owned entities of the room (props, mirrors, ...).
			await session.SyncEntities();

			return true;
		}
	}
}