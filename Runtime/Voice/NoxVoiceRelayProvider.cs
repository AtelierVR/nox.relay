using System;
using Nox.Avatars;
using Nox.Avatars.Voice;
using Nox.CCK.Utils;
using Nox.Relay.Core.Types.Stream;
using Nox.Relay.Runtime.Physicals;
using UnityEngine;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>
	/// Relay network provider — bridges NoxVoiceChat with the QUIC relay.
	/// Handles avatar lifecycle: creates voice on a temporary anchor if no avatar,
	/// migrates to VoiceAvatarModule's AudioSource when the avatar loads.
	/// </summary>
	[RequireComponent(typeof(NoxVoiceChat))]
	public class NoxVoiceRelayProvider : MonoBehaviour, INoxVoiceNetProvider {
		public NoxVoiceChat VoiceChat { get; private set; }

		public uint ChannelId = 0;
		public int MaxDataBytesPerPacket = 1000;

		private Core.Rooms.Room _room;
		private bool _isLocal;
		private bool _started;

		// ── Avatar migration ──
		private RemotePhysical _physical;
		private GameObject _anchor;
		private int _playerId = -1;

		bool INoxVoiceNetProvider.IsLocalPlayerDeafened => _isLocal && VoiceChat?.IsDeafened == true;

		private void Awake() {
			VoiceChat = GetComponent<NoxVoiceChat>();
			EnsureComponents();
		}

		/// <summary>Ensure NoxVoiceChat + NoxVoiceAudioSourceOutput exist on this GameObject.</summary>
		private void EnsureComponents() {
			if (VoiceChat == null)
				VoiceChat = gameObject.AddComponent<NoxVoiceChat>();

			if (VoiceChat.Config == null) {
				VoiceChat.Config = Main.CoreAPI?.AssetAPI?.GetAsset<NoxVoiceConfig>("config.asset");
				if (VoiceChat.Config == null) {
					VoiceChat.Config = ScriptableObject.CreateInstance<NoxVoiceConfig>();
					VoiceChat.Config.Init();
				}
			}

			if (VoiceChat.AudioOutput == null)
				VoiceChat.AudioOutput = gameObject.GetOrAddComponent<NoxVoiceAudioSourceOutput>();
		}

		/// <summary>Initialize for a remote player (receiving voice).</summary>
		public void InitializeRemote(Core.Rooms.Room room, int playerId) {
			_room = room;
			_isLocal = false;
			_playerId = playerId;

			// Try to set up on the RemotePhysical
			_physical = GetComponent<RemotePhysical>();
			if (_physical != null) {
				_physical.ActuallyDestroyed.AddListener(OnPhysicalDestroyed);
				_physical.OnAvatarSet.AddListener(OnAvatarSet);
				VoiceChat.Player = _physical.Reference;
				VoiceChat.BindPlayerEvents();
			}

			CreateOrMigrateOutput();

			if (!_started) {
				VoiceChat.StartClient(this, false, MaxDataBytesPerPacket);
				_started = true;
			}
		}

		/// <summary>Initialize for the local player (sending voice).</summary>
		public void InitializeLocal(Core.Rooms.Room room) {
			_room = room;
			_isLocal = true;

			EnsureComponents();

			if (VoiceChat.AudioInput == null)
				VoiceChat.AudioInput = gameObject.GetOrAddComponent<NoxVoiceMicInput>();

			if (!_started) {
				VoiceChat.StartClient(this, true, MaxDataBytesPerPacket);
				_started = true;
			}
		}

		// ── Avatar lifecycle ──

		private void OnAvatarSet(IRuntimeAvatar avatar) {
			_runtimeAvatar = avatar;
			CreateOrMigrateOutput();
		}

		private IRuntimeAvatar _runtimeAvatar;

		private void CreateOrMigrateOutput() {
			if (_isLocal || _physical == null) return;

			// Check if avatar has a voice module with a valid AudioSource
			var voiceModules = _runtimeAvatar?.Descriptor?.GetModules<IVoiceModule>();
			var avatarSource = (voiceModules?.Length > 0)
				? voiceModules[0].GetSource()
				: null;

			if (avatarSource != null) {
				// Avatar provides a voice AudioSource — destroy fallback anchor and migrate
				if (_anchor != null) {
					_anchor.Destroy();
					_anchor = null;
				}

				// Recreate output on this GameObject (may have been on destroyed anchor)
				var output = gameObject.GetOrAddComponent<NoxVoiceAudioSourceOutput>();
				output.VoiceChat = VoiceChat;
				VoiceChat.AudioOutput = output;

				output.SetSource(avatarSource);
				return;
			}

			// No avatar voice source — ensure fallback "Voice" anchor exists at local origin
			if (_anchor == null) {
				_anchor = new GameObject("Voice");
				_anchor.transform.SetParent(_physical.transform, false);
				_anchor.transform.localPosition = Vector3.zero;
				_anchor.transform.localRotation = Quaternion.identity;

				var anchorOutput = _anchor.AddComponent<NoxVoiceAudioSourceOutput>();
				anchorOutput.VoiceChat = VoiceChat;
				VoiceChat.AudioOutput = anchorOutput;

				// Remove old output on this GameObject (replaced by anchor's)
				var oldOutput = GetComponent<NoxVoiceAudioSourceOutput>();
				if (oldOutput != null && oldOutput != anchorOutput)
					oldOutput.Destroy();
			}
		}

		private void OnPhysicalDestroyed() {
			if (_anchor != null) {
				_anchor.Destroy();
				_anchor = null; 
			}
			if (_physical != null) {
				_physical.ActuallyDestroyed.RemoveListener(OnPhysicalDestroyed);
				_physical.OnAvatarSet.RemoveListener(OnAvatarSet);
				_physical = null;
			}

			// Unregister from session so next voice frame triggers recreation on new physical
			if (_playerId >= 0 && Session.Current != null)
				Session.Current.UnregisterVoiceProvider(_playerId);
		}

		/// <summary>
		/// Handle incoming voice data from the relay.
		/// </summary>
		public void ReceiveRelayFrame(StreamEvent voiceEvent) {
			if (!_started || VoiceChat == null) return;

			// Update distance mode on the output if it changed
			var mode = VoiceDistanceModeExtensions.FromLevelFlags(voiceEvent.LevelFlags);
			var output = VoiceChat.AudioOutput as NoxVoiceAudioSourceOutput;
			if (output != null && output.DistanceMode != mode) {
				output.DistanceMode = mode;
				output.ApplySpatialSettings();
			}

			VoiceChat.ReceiveFrame(
				index: voiceEvent.FrameIndex,
				timestamp: voiceEvent.Timestamp > 0 ? voiceEvent.Timestamp : Time.timeAsDouble,
				additionalLatency: Time.deltaTime,
				data: voiceEvent.Sample ?? ReadOnlySpan<byte>.Empty
			);
		}

		void INoxVoiceNetProvider.RelayFrame(int index, double timestamp, ReadOnlySpan<byte> data) {
			if (_room == null || !_started) return;

			byte[] sample = data.IsEmpty ? Array.Empty<byte>() : data.ToArray();
			var flags = (VoiceChat?.Config?.DefaultDistanceMode ?? VoiceDistanceMode.Normal)
				.ToLevelFlags();
			_ = _room.Stream(StreamRequest.MakeSample(flags, sample, index, timestamp));
		}

		private void OnDestroy() {
			if (_started) {
				VoiceChat?.StopClient();
				_started = false;
			}
			OnPhysicalDestroyed();
		}
	}
}
