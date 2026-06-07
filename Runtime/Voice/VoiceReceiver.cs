using System.Linq;
using Nox.Avatars;
using Nox.CCK.Avatars.Voice;
using Nox.Microphone.Players;
using Nox.Microphone.Runtime;
using Nox.Relay.Runtime.Players;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>
	/// Reçoit les paquets Opus d'un remote player, les décode et les envoie
	/// au VoicePlayback. Pattern identique au Receive() du test.
	/// Une instance par remote player, créée par VoiceManager.
	/// </summary>
	public class VoiceReceiver {
		private readonly RemotePlayer      _player;
		private readonly VoiceManager      _manager;
		private readonly OpusDecoder.OpusDecoderInstance _decoder;

		private VoicePlayback _playback;
		private bool _disposed;
		private bool _migrated;

		private Physicals.RemotePhysical _physical;
		private GameObject _anchor;

		public int  PlayerId    => _player.Id;
		public bool IsReceiving { get; private set; }

		private int FrameSize  => _manager.Config?.FrameSize  ?? 960;
		private int SampleRate => _manager.Config?.SampleRate ?? 48000;

		public VoiceReceiver(RemotePlayer player, VoiceManager manager) {
			_player  = player;
			_manager = manager;
			_decoder = new OpusDecoder.OpusDecoderInstance(SampleRate, 1);
			TryCreatePlayback();
		}

		// ── Playback lazy creation (le physical peut ne pas exister encore) ──

		private void TryCreatePlayback() {
			if (_playback != null) return;
			if (!_player.TryGetPhysical<Physicals.RemotePhysical>(out var p)) return;

			_physical = p;

			// Si le VoiceAvatarModule est déjà chargé, on l'utilise directement
			var src = GetAvatarSource(p);
			if (src != null) {
				_playback = new VoicePlayback(src.gameObject, _manager.Config);
				_migrated = true;
			} else {
				_anchor = new GameObject("VoiceAnchor");
				_anchor.transform.SetParent(p.transform, false);
				_playback = new VoicePlayback(_anchor, _manager.Config);
				_migrated = false;
			}

			_playback.ApplyMode(_manager.GetOrCreatePermissions(_player).Speak);
			p.ActuallyDestroyed.AddListener(OnDestroyed);
			p.OnAvatarSet.AddListener(OnAvatarSet);
		}

		// ── Migration vers le VoiceAvatarModule ──

		private void OnAvatarSet(IRuntimeAvatar _) => TryMigrate();

		private void TryMigrate() {
			if (_playback == null || _migrated || _physical == null) return;
			var src = GetAvatarSource(_physical);
			if (src == null) return;

			_playback.SetSource(src);
			_migrated = true;
			if (_anchor) { Object.Destroy(_anchor); _anchor = null; }
		}

		private static AudioSource GetAvatarSource(Physicals.RemotePhysical p) {
			var m = p.GetComponentInChildren<VoiceAvatarModule>(true);
			return m ? m.GetSource() : null;
		}

		private void OnDestroyed() {
			_anchor = null;
			_playback?.Dispose(); _playback = null;
			if (_physical != null) {
				_physical.ActuallyDestroyed.RemoveListener(OnDestroyed);
				_physical.OnAvatarSet.RemoveListener(OnAvatarSet);
				_physical = null;
			}
			_migrated = false;
		}

		// ── HandleVoiceData = comme le Receive() du test ──

		public void HandleVoiceData(byte[] opusData, SpeakMode mode) {
			if (_disposed || _decoder == null) return;

			TryCreatePlayback();
			if (_playback == null) return;

			if (!_migrated) TryMigrate();
			_playback.ApplyMode(mode);

			try {
				float[] pcm = _decoder.Decode(opusData, FrameSize);
				if (pcm != null && pcm.Length > 0) {
					_playback.Feed(pcm);
					IsReceiving = true;
				}
			} catch (System.Exception ex) {
				Debug.LogWarning($"[VoiceReceiver] Decode error: {ex.Message}");
				IsReceiving = false;
			}
		}

		public void OnVoiceEnded() => IsReceiving = false;

		public void Dispose() {
			if (_disposed) return;
			_disposed = true;
			if (_physical != null) {
				_physical.ActuallyDestroyed.RemoveListener(OnDestroyed);
				_physical.OnAvatarSet.RemoveListener(OnAvatarSet);
				_physical = null;
			}
			if (_anchor) Object.Destroy(_anchor);
			_playback?.Dispose();
			_decoder?.Dispose();
		}
	}
}
