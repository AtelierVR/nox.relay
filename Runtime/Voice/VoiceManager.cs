using System.Collections.Generic;
using Nox.Microphone.Players;
using Nox.Players;
	using Nox.Relay.Core.Types.Stream;
using Nox.Relay.Runtime.Players;
using UnityEngine;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>
	/// Central orchestrator for the modular voice chat system.
	/// Manages <see cref="VoicePermissions"/> for all players, routes incoming
	/// <see cref="VoiceEvent"/> packets, and coordinates <see cref="VoiceSender"/>
	/// / <see cref="VoiceReceiver"/> lifecycles.
	/// <para>
	/// Created and owned by <see cref="Session"/>.
	/// </para>
	/// </summary>
	public class VoiceManager {
		internal readonly Session Session;
		internal VoiceConfig Config;

		/// <summary>
		/// Per-player permission + state entries. Keyed by player entity ID.
		/// </summary>
		internal readonly Dictionary<int, VoicePermissions> Permissions = new();

		/// <summary>
		/// VoiceSender instances keyed by "{playerId}_{channelId:X8}".
		/// Multiple senders per player for different channels.
		/// </summary>
		internal readonly Dictionary<string, VoiceSender> Senders = new();

		/// <summary>
		/// Default channels to create VoiceSender instances for.
		/// </summary>
		private static readonly uint[] DefaultChannels = { ChannelId.Voice };

		/// <summary>
		/// VoiceReceiver instances keyed by remote player entity ID.
		/// </summary>
		internal readonly Dictionary<int, VoiceReceiver> Receivers = new();

		public VoiceManager(Session session, VoiceConfig config = null) {
			Session = session;
			Config  = config ?? ScriptableObject.CreateInstance<VoiceConfig>();

			if (Session.Room != null)
				Session.Room.OnStream.AddListener(HandleVoiceEvent);

			Session.OnPlayerJoined.AddListener(OnPlayerJoined);
			Session.OnPlayerLeft.AddListener(OnPlayerLeft);
		}

		// ──────────────────────────────────────────────
		//  Player lifecycle
		// ──────────────────────────────────────────────

		private void OnPlayerJoined(IPlayer player) {
			var permissions = GetOrCreatePermissions(player);
			permissions.Config = Config;

			// Create VoiceReceiver for remote players
			if (!player.IsLocal && player is RemotePlayer remotePlayer) {
				var receiver = new VoiceReceiver(remotePlayer, this);
				Receivers[remotePlayer.Id] = receiver;
			}

			// Create VoiceSender(s) for the local player (one per default channel)
			if (player.IsLocal && player is LocalPlayer localPlayer) {
				foreach (var channelId in DefaultChannels) {
					var sender = new VoiceSender(localPlayer, this, channelId);
					Senders[sender.Key] = sender;
				}
			}
		}

		private void OnPlayerLeft(IPlayer player) {
			var id = ((Players.Player)player).Id;

			if (Receivers.TryGetValue(id, out var receiver)) {
				receiver.Dispose();
				Receivers.Remove(id);
			}

			// Remove all senders for this player
			var keysToRemove = new List<string>();
			foreach (var kv in Senders) {
				if (kv.Value.ChannelId != 0 && kv.Key.StartsWith($"{id}_")) {
					kv.Value.Dispose();
					keysToRemove.Add(kv.Key);
				}
			}
			foreach (var key in keysToRemove)
				Senders.Remove(key);

			Permissions.Remove(id);
		}

		internal VoicePermissions GetOrCreatePermissions(IPlayer player) {
			var id = ((Players.Player)player).Id;
			if (!Permissions.TryGetValue(id, out var perms)) {
				perms = new VoicePermissions { Config = Config };
				Permissions[id] = perms;
			}
			return perms;
		}

		// ──────────────────────────────────────────────
		//  Voice event routing
		// ──────────────────────────────────────────────

		private void HandleVoiceEvent(StreamEvent voiceEvent) {
			switch (voiceEvent.SubType) {
				case StreamSubType.Sample:
					HandleVoiceSample(voiceEvent);
					break;
				case StreamSubType.Control:
					HandleVoiceControl(voiceEvent);
					break;
			}
		}

		private void HandleVoiceSample(StreamEvent voiceEvent) {
			var localPlayer = Session.LocalPlayer;
			if (localPlayer == null)
				return;

			int localId  = localPlayer.Id;
			int remoteId = voiceEvent.PlayerId;

			if (remoteId == localId)
				return;

			// Get or create permissions for both players
			var localPerms  = GetOrCreatePermissions(localPlayer);
			var remotePerms = Permissions.TryGetValue(remoteId, out var existingPerms) ? existingPerms : null;

			if (remotePerms == null) {
				remotePerms = new VoicePermissions { Config = Config };
				Permissions[remoteId] = remotePerms;
			}

			// Decode speak mode from wire (3-bit)
			var speakMode = WireToSpeak((byte)(voiceEvent.DistanceMode & 0b_0000_0111));
			remotePerms.Speak = speakMode;

			var remotePlayer = Session.Entities.GetEntity<Players.Player>(remoteId);
			if (remotePlayer == null)
				return;

			bool canHear = VoicePermissions.CanHear(
				localId, localPlayer.Position,
				remoteId, remotePlayer.Position,
				speakMode,
				localPerms
			);

			if (!canHear)
				return;

			// Route to VoiceReceiver
			localPerms.ReceivingFrom.Add(remoteId);
			if (Receivers.TryGetValue(remoteId, out var receiver)) {
				receiver.HandleVoiceData(voiceEvent.Sample, speakMode);
			} else if (remotePlayer is RemotePlayer rp) {
				receiver = new VoiceReceiver(rp, this);
				Receivers[remoteId] = receiver;
				receiver.HandleVoiceData(voiceEvent.Sample, speakMode);
			}
		}

		private void HandleVoiceControl(StreamEvent voiceEvent) {
			// Server acknowledged a hearing control change.
			// The server now filters future voice broadcasts accordingly.
			Logger.LogDebug(
				$"[VoiceManager] Control acked: listener={voiceEvent.ListenerId}, " +
				$"speaker={voiceEvent.SpeakerId}, flags={voiceEvent.ControlFlags}");
		}

		// ──────────────────────────────────────────────
		//  Resolved queries
		// ──────────────────────────────────────────────

		/// <summary>
		/// Returns all players the given listener can currently hear.
		/// </summary>
		public IPlayer[] ResolveHearablePlayers(IPlayer listener) {
			var listenerPlayer = listener as Players.Player;
			if (listenerPlayer == null)
				return System.Array.Empty<IPlayer>();

			int listenerId = listenerPlayer.Id;
			var listenerPerms = GetOrCreatePermissions(listener);

			var allPlayers = Session.Entities.GetEntities<Players.Player>();
			var result = new List<IPlayer>();

			foreach (var other in allPlayers) {
				if (other.Id == listenerId)
					continue;

				var otherPerms = Permissions.TryGetValue(other.Id, out var op) ? op : null;
				if (otherPerms == null)
					continue;

				if (VoicePermissions.CanHear(
					listenerId, listenerPlayer.Position,
					other.Id, other.Position,
					otherPerms.Speak,
					listenerPerms
				)) {
					result.Add(other);
				}
			}

			return result.ToArray();
		}

		/// <summary>
		/// Returns all players who can currently hear the given speaker.
		/// Used for the send optimization: if this returns empty, skip microphone capture.
		/// </summary>
		public IPlayer[] ResolveListeningPlayers(IPlayer speaker) {
			var speakerPlayer = speaker as Players.Player;
			if (speakerPlayer == null)
				return System.Array.Empty<IPlayer>();

			int speakerId = speakerPlayer.Id;
			var speakerPerms = GetOrCreatePermissions(speaker);

			if (speakerPerms.Speak == SpeakMode.Muted)
				return System.Array.Empty<IPlayer>();

			var allPlayers = Session.Entities.GetEntities<Players.Player>();
			var result = new List<IPlayer>();

			foreach (var other in allPlayers) {
				if (other.Id == speakerId)
					continue;

				if (VoicePermissions.CanBeHeardBy(
					speakerId, speakerPlayer.Position,
					speakerPerms.Speak,
					other.Id, other.Position,
					speakerPerms
				)) {
					result.Add(other);
				}
			}

			return result.ToArray();
		}

		// ──────────────────────────────────────────────
		//  Wire helpers
		// ──────────────────────────────────────────────

		private static SpeakMode WireToSpeak(byte b) => b switch {
			0 => SpeakMode.Whisper,
			1 => SpeakMode.Normal,
			2 => SpeakMode.Broadcast,
			_ => SpeakMode.Normal,
		};

		// ──────────────────────────────────────────────
		//  Hearing Control
		// ──────────────────────────────────────────────

		/// <summary>
		/// Send a hearing control request to the server.
		/// Tells the server which players can hear each other on a specific channel.
		/// The server filters future voice broadcast accordingly.
		/// </summary>
		public void SendVoiceControl(ushort listenerId, ushort speakerId, bool canHear) {
			if (Session.Room == null)
				return;

			_ = Session.Room.Stream(StreamRequest.MakeControl(listenerId, speakerId, canHear));
		}

		// ──────────────────────────────────────────────
		//  Update
		// ──────────────────────────────────────────────

		/// <summary>
		/// Called each frame by <see cref="Session.Update"/>.
		/// Updates all active VoiceSenders.
		/// </summary>
		public void Update() {
			int senderCount = Senders.Count;
			if (senderCount > 0) {
				foreach (var sender in Senders.Values) {
					sender.Update();
				}
			}
		}

		// ──────────────────────────────────────────────
		//  Cleanup
		// ──────────────────────────────────────────────

		public void Dispose() {
			if (Session.Room != null) {
				Session.Room.OnStream.RemoveListener(HandleVoiceEvent);
			}

			Session.OnPlayerJoined.RemoveListener(OnPlayerJoined);
			Session.OnPlayerLeft.RemoveListener(OnPlayerLeft);

			Permissions.Clear();
			Senders.Clear();
			Receivers.Clear();
		}
	}
}
