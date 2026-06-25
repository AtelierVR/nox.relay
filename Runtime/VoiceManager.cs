using System;
using System.Collections.Generic;
using Nox.Relay.Core.Rooms;
using Nox.Relay.Core.Types.Stream;
using Nox.Relay.Runtime.Voice;
using UnityEngine;
using Logger = Nox.CCK.Utils.Logger;
using Object = UnityEngine.Object;

namespace Nox.Relay.Runtime {
	/// <summary>
	/// Manages all voice chat routing for a relay session.
	/// Owns provider registration, broadcast receivers, and stream event dispatch.
	/// </summary>
	internal sealed class VoiceManager {
		private readonly Session _session;

		private bool _routingSetup;
		private readonly Dictionary<int, NoxVoiceRelayProvider> _providers = new();
		private readonly Dictionary<int, NoxVoiceBroadcastReceiver> _broadcastReceivers = new();
		private GameObject _broadcastRoot;

		public VoiceManager(Session session) {
			_session = session;
		}

		/// <summary>Attach stream listener to the room (idempotent).</summary>
		public void SetupRouting(Room room) {
			if (_routingSetup || room == null) return;
			_routingSetup = true;

			room.OnStream.AddListener(OnVoiceStream);
			Logger.LogDebug("[VoiceManager] Voice routing set up via Room.OnStream", tag: nameof(VoiceManager));
		}

		/// <summary>Register a voice provider for a player.</summary>
		public void RegisterProvider(int playerId, NoxVoiceRelayProvider provider) {
			_providers[playerId] = provider;
		}

		/// <summary>Unregister a voice provider for a player.</summary>
		public void UnregisterProvider(int playerId) {
			_providers.Remove(playerId);
		}

		/// <summary>Remove and destroy a broadcast receiver for a player.</summary>
		public void RemoveBroadcastReceiver(int playerId) {
			if (_broadcastReceivers.TryGetValue(playerId, out var receiver)) {
				if (receiver != null)
					Object.Destroy(receiver.gameObject);
				_broadcastReceivers.Remove(playerId);
			}
		}

		/// <summary>Handle an incoming stream event from the relay.</summary>
		private void OnVoiceStream(StreamEvent voiceEvent) {
			int speakerId = voiceEvent.PlayerId;
			var mode = VoiceDistanceModeExtensions.FromLevelFlags(voiceEvent.LevelFlags);

			// ── Broadcast mode — create a global 2D receiver if no physical exists ──
			if (mode == VoiceDistanceMode.Broadcast) {
				if (!_providers.TryGetValue(speakerId, out var provider) || provider?.gameObject == null) {
					// No physical for this speaker — use/create a broadcast receiver
					if (!_broadcastReceivers.TryGetValue(speakerId, out var receiver) || receiver == null) {
						receiver = CreateBroadcastReceiver(speakerId);
						if (receiver == null) return;
					}
					receiver.ReceiveFrame(
						voiceEvent.FrameIndex,
						voiceEvent.Timestamp > 0 ? voiceEvent.Timestamp : Time.timeAsDouble,
						Time.deltaTime,
						voiceEvent.Sample ?? ReadOnlySpan<byte>.Empty);
					return;
				}
				// Physical exists — fall through to normal provider path
			}

			// Provider is registered on physical creation, unregistered on destruction.
			// If no provider exists, the physical isn't ready yet — drop the frame.
			if (!_providers.TryGetValue(speakerId, out var normalProvider) || normalProvider?.gameObject == null)
				return;

			normalProvider.ReceiveRelayFrame(voiceEvent);
		}

		private NoxVoiceBroadcastReceiver CreateBroadcastReceiver(int speakerId) {
			if (_broadcastRoot == null) {
				_broadcastRoot = new GameObject("VoiceBroadcastReceivers");
				Object.DontDestroyOnLoad(_broadcastRoot);
			}

			var go = new GameObject($"BroadcastReceiver_{speakerId}");
			go.transform.SetParent(_broadcastRoot.transform, false);

			var receiver = go.AddComponent<NoxVoiceBroadcastReceiver>();
			var config = Main.CoreAPI?.AssetAPI?.GetAsset<NoxVoiceConfig>("config.asset");
			if (config == null) {
				config = ScriptableObject.CreateInstance<NoxVoiceConfig>();
				config.Init();
			}

			receiver.Initialize(speakerId, config, _session.Room);
			_broadcastReceivers[speakerId] = receiver;

			Logger.LogDebug(
				$"[VoiceManager] Created broadcast receiver for speaker {speakerId}",
				tag: nameof(VoiceManager));
			return receiver;
		}

		/// <summary>Clean up all broadcast receivers and the root GameObject.</summary>
		public void Dispose() {
			foreach (var kvp in _broadcastReceivers) {
				if (kvp.Value != null)
					Object.Destroy(kvp.Value.gameObject);
			}
			_broadcastReceivers.Clear();

			if (_broadcastRoot != null) {
				Object.Destroy(_broadcastRoot);
				_broadcastRoot = null;
			}

			_providers.Clear();
		}
	}
}
