using Nox.Avatars;
using Nox.CCK.Audio.Opus;
using Nox.Relay.Core.Types.Stream;
using UnityEngine;

namespace Nox.Relay.Runtime.Players {
	/// <summary>
	/// Voice of a remote player: incoming frames are handed to an <see cref="OpusAudioReceiver"/>, which
	/// buffers them in the sender's order and decodes them on a steady frame cadence, and the decoded
	/// audio is played by a spatialized <see cref="AudioSource"/>. The playback is exposed as
	/// <see cref="Player.Audio"/> so the UI can read its <c>Level</c>.
	/// </summary>
	/// <remarks>
	/// The clip is played through the <see cref="VoiceProvider"/> matching the current state, which
	/// owns its output and everything that output needs: <see cref="AvatarVoiceProvider"/> while the
	/// avatar publishes a source through its <see cref="Nox.Avatars.Voice.IVoiceModule"/> (placed on the
	/// head bone), <see cref="PhysicalVoiceProvider"/> for a fallback anchored on the physical, and
	/// <see cref="UnattachedVoiceProvider"/> when no physical can place the speaker. A fallback output
	/// lives in its own root object rather than under the physical: render culling destroys the
	/// physical, and a child would be deactivated and destroyed with it, silencing a player who is
	/// still speaking.
	/// </remarks>
	/// <remarks>
	/// The ring buffer only plays correctly when its write head advances at the sample rate, i.e. one
	/// frame per frame duration of <b>real</b> time. The network does not guarantee that (bursts,
	/// late/duplicate packets), so the write is paced here instead of on arrival: without it the write
	/// head drifts from the <see cref="AudioSource"/> read head and the ring starts replaying/tearing.
	/// </remarks>
	/// <remarks>
	/// Datagrams are reordered, duplicated and lost in transit: the jitter buffer that copes with that is
	/// <see cref="OpusAudioReceiver"/> itself (see <c>ReceiveFrame</c> / <c>PopFrame</c>), so any consumer
	/// of the pipeline gets the same behaviour. This partial only paces that playout — one slot per frame
	/// duration of real time, whatever the arrival pattern is — and routes the clip to an output.
	/// </remarks>
	public partial class RemotePlayer {
		/// <summary>RMS level above which the player is flagged as speaking.</summary>
		private const float SpeakingThreshold = 0.02f;

		/// <summary>Frame duration of the pipeline, matching the sender's <c>OpusConstants</c> default.</summary>
		private const float FrameSeconds = OpusConstants.DefaultFrameDurationMs / 1000f;

		/// <summary>Jitter cushion kept between the read head and the write head, in seconds.</summary>
		private const float TargetLatencySeconds = 0.06f;

		/// <summary>Cap on the real time a single update may compensate (never flush a burst at once).</summary>
		private const int MaxCatchUpFrames = 4;

		private OpusAudioReceiver _voiceReceiver;

		/// <summary>
		/// Provider of the current output: it owns the object, the source and its placement, and is
		/// replaced as soon as it stops matching the player (<see cref="VoiceProvider.Valid"/>).
		/// </summary>
		private VoiceProvider _provider;

		/// <summary>
		/// Distance mode announced by the speaker's last sample. Only broadcast travels everywhere, so it
		/// is the only mode that stays audible while no physical can place the speaker.
		/// </summary>
		private bool _voiceBroadcast;

		private float _playoutAccumulator;
		private bool _playing;

		/// <summary>True once the jitter cushion has been buffered (playback may start or resume).</summary>
		private bool _primed;

		/// <summary>Hearing gate from the room: true while the local listener may not hear this speaker.</summary>
		private bool _hearingBlocked;

		/// <summary>True while a live physical can place the source in the world.</summary>
		internal bool VoiceAnchored
			=> Physical != null && !Physical.IsDestroying;

		/// <summary>Transform the fallback output follows, or null when the speaker cannot be placed.</summary>
		internal Transform VoiceAnchor
			=> Physical ? Physical.transform : null;

		/// <summary>Distance mode announced by the speaker's last sample.</summary>
		internal bool VoiceBroadcast
			=> _voiceBroadcast;

		/// <summary>
		/// Starts the playback pipeline, creating it on first use (idempotent), and routes the clip to
		/// the best output. Called when the physical is created, when the avatar changes, and lazily on
		/// the first voice packet — so a speaker is heard even before (or without) a physical.
		/// </summary>
		internal void StartVoice() {
			if (_voiceReceiver == null) {
				_voiceReceiver = new OpusAudioReceiver(OpusConstants.SampleRate48k, clipName: $"Voice_{Id}");
				Audio          = _voiceReceiver;

				OnVolume.AddListener(OnVoiceVolumeChanged);
				OnMute.AddListener(OnVoiceMuteChanged);
				OnAvatarLoaded.AddListener(OnVoiceAvatarLoaded);

				ResetVoiceTimeline();
			}

			RefreshVoiceOutput();
		}

		/// <summary>Stops and releases the playback pipeline. Safe to call when it never started.</summary>
		internal void StopVoice() {
			if (_voiceReceiver != null) {
				OnVolume.RemoveListener(OnVoiceVolumeChanged);
				OnMute.RemoveListener(OnVoiceMuteChanged);
				OnAvatarLoaded.RemoveListener(OnVoiceAvatarLoaded);
				Audio = null;
				_voiceReceiver.Dispose();
				_voiceReceiver = null;
			}

			_provider?.Dispose();

			_provider       = null;
			_voiceBroadcast = false;
			_hearingBlocked = false;

			ResetVoiceTimeline();
			IsSpeaking = false;
		}

		/// <summary>Releases the voice pipeline when the player is disposed (culling only detaches it).</summary>
		public override void Dispose() {
			StopVoice();
			base.Dispose();
		}

		/// <summary>Conceals stalled streams and refreshes the speaking indicator.</summary>
		public override void Update() {
			base.Update();
			TickVoice();
		}

		/// <summary>
		/// Emits at most one frame per <see cref="FrameSeconds"/> of real time, so the ring buffer's
		/// write head advances at the sample rate — the same pace as the AudioSource's read head —
		/// whatever the packet arrival pattern is.
		/// </summary>
		internal void TickVoice() {
			if (_voiceReceiver == null)
				return;

			_playoutAccumulator += Time.unscaledDeltaTime;

			// A long stall (focus loss, domain reload) must not flush its whole debt as a burst.
			var maxCatchUp = FrameSeconds * MaxCatchUpFrames;
			if (_playoutAccumulator > maxCatchUp)
				_playoutAccumulator = maxCatchUp;

			while (_playoutAccumulator >= FrameSeconds) {
				_playoutAccumulator -= FrameSeconds;
				EmitVoiceFrame();
			}

			RefreshVoiceOutput();

			// Report the level of the frame being heard, not the one just written: the write head runs
			// one cushion (TargetLatencySeconds) ahead of the AudioSource read head.
			var output = _provider?.Source;
			_voiceReceiver.ReadPosition = output != null && output.isPlaying
				? output.timeSamples
				: -1;

			IsSpeaking = _voiceReceiver.Level > SpeakingThreshold;
		}

		/// <summary>Audio sample sent by this player; buffered by frame index, not decoded on arrival.</summary>
		protected override void OnVoiceSample(StreamEvent @event) {
			if (_voiceReceiver == null) {
				StartVoice();
				if (_voiceReceiver == null)
					return;
			}

			_voiceBroadcast = (@event.LevelFlags & StreamLevelFlags.DistanceMode_Mask) == StreamLevelFlags.DistanceMode_Broadcast;

			// Ordering, dedupe and loss handling belong to the receiver: it knows the frame index.
			_voiceReceiver.ReceiveFrame(@event.FrameIndex, @event.Sample);
		}

		/// <summary>
		/// Hearing control: the room tells whether the local listener may hear this speaker, which
		/// gates this player's playback.
		/// </summary>
		protected override void OnVoiceControl(StreamEvent @event) {
			if (Context.Context.InterEntities.LocalId != @event.ListenerId)
				return;

			_hearingBlocked = (@event.ControlFlags & 0b_0000_0001) == 0;
			ApplyVoiceOutput();
		}

		/// <summary>
		/// Writes the slot the sender's timeline expects next into the ring: the buffered frame, a concealed
		/// (PLC) one while the stream is merely late, or silence once it stopped.
		/// </summary>
		private void EmitVoiceFrame() {
			_voiceReceiver.PopFrame();
			TryStartPlayback();
		}

		/// <summary>
		/// Locks the read head <see cref="TargetLatencySeconds"/> behind the write head: both heads then
		/// advance at the sample rate, so the cushion is kept without any further correction.
		/// </summary>
		private void TryStartPlayback() {
			var source = _provider?.Source;
			if (_playing || source == null)
				return;

			// The first start waits for the jitter cushion; after an output swap the ring already
			// holds it, so playback resumes on the next frame.
			if (!_primed) {
				if (_voiceReceiver.BufferedFrames * FrameSeconds < TargetLatencySeconds)
					return;

				_primed = true;
			}

			source.Play();
			source.timeSamples = ReadTargetSamples();
			_playing           = true;
		}

		/// <summary>Read position keeping one cushion behind the write head.</summary>
		private int ReadTargetSamples() {
			var clip           = _voiceReceiver.Clip;
			var latencySamples = Mathf.RoundToInt(TargetLatencySeconds * OpusConstants.SampleRate48k);
			var target         = (_voiceReceiver.Position - latencySamples) % clip.samples;
			return (target + clip.samples) % clip.samples;
		}

		private void ResetVoiceTimeline() {
			_playoutAccumulator = 0f;
			_playing            = false;
			_primed             = false;

			if (_voiceReceiver == null)
				return;

			_voiceReceiver.ReadPosition = -1;
			_voiceReceiver.ResetTimeline();
		}

		/// <summary>
		/// Replaces the provider when it is gone or no longer matches the player, places its output and
		/// applies the player's volume and mute. Called every frame.
		/// </summary>
		/// <param name="force">Rebuilds the provider even when the current one is still valid.</param>
		private void RefreshVoiceOutput(bool force = false) {
			if (_voiceReceiver == null)
				return;

			if (force || _provider == null || !_provider.Valid)
				AttachVoiceProvider();

			_provider.Place();

			ApplyVoiceOutput();
		}

		/// <summary>
		/// Builds the provider for the current state and hands it the clip, when its output is not the
		/// one that was already playing.
		/// </summary>
		private void AttachVoiceProvider() {
			var previous = _provider?.Source;
			_provider?.Dispose();

			_provider = VoiceProvider.Create(this);

			// Stop the previous output before moving the clip: both would play it otherwise.
			if (previous != null) {
				previous.Stop();
				previous.clip = null;
			}

			_playing = false;

			var source = _provider.Source;
			if (source != null) {
				source.clip        = _voiceReceiver.Clip;
				source.loop        = true;
				source.playOnAwake = false;
			}
		}

		/// <summary>Avatar (re)loaded: its voice module publishes the new output, so rebuild on it.</summary>
		private void OnVoiceAvatarLoaded(IRuntimeAvatar avatar)
			=> RefreshVoiceOutput(force: true);

		private void OnVoiceVolumeChanged(float local, float effective)
			=> ApplyVoiceOutput();

		private void OnVoiceMuteChanged(bool local, bool effective)
			=> ApplyVoiceOutput();

		private void ApplyVoiceOutput() {
			var source = _provider?.Source;
			if (source == null)
				return;

			source.volume = EffectiveVolume;

			// A provider gates its own output when it cannot be heard (detached, and not broadcasting): a
			// source placed in the world is already filtered by distance and by the room's hearing rules.
			source.mute = IsEffectivelyMuted || _hearingBlocked || _provider.Gated;
		}
	}
}
