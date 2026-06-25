using System;
using Nox.CCK.Scripting;
using Nox.Entities;
using Nox.Scripting;
using UnityEngine;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>
	/// Scripting module <c>"voice"</c> — voice chat settings exposed to world scripts.
	/// <code>
	/// import { distanceMode, volume, muted, deafened } from 'voice';
	/// distanceMode = 2;  // 0=Normal, 1=Whisper, 2=Broadcast
	/// volume = 0.8;       // 0.0 to 1.0
	/// muted = true;       // mute local microphone
	/// </code>
	/// </summary>
	public static class VoiceScriptingModule {
		public static readonly IScriptingModuleDefinition Module =
			ScriptingModuleBuilder.Create("voice")
				.WithTags("session")
				// ── Distance mode (getter/setter) ──
				.AddVariable("distanceMode",
					getter: () => {
						var cfg = GetConfig();
						return cfg != null ? (double)(byte)cfg.DefaultDistanceMode : 0.0;
					},
					setter: v => {
						var cfg = GetConfig();
						if (cfg == null) return;
						var mode = (VoiceDistanceMode)(byte)Math.Clamp((double)v, 0, 2);
						cfg.DefaultDistanceMode = mode;
						ApplyToLocalOutput(mode);
					})
				// ── Volume (getter/setter) ──
				.AddVariable("volume",
					getter: () => {
						var output = GetLocalOutput();
						return output?.AudioSource != null
							? (double)output.AudioSource.volume
							: 1.0;
					},
					setter: v => {
						float vol = Mathf.Clamp01((float)(double)v);
						var output = GetLocalOutput();
						if (output?.AudioSource != null)
							output.AudioSource.volume = vol;
					})
				// ── Muted (getter/setter) ──
				.AddVariable("muted",
					getter: () => {
						var vc = GetLocalVoiceChat();
						return vc != null ? vc.IsInputMuted : false;
					},
					setter: v => {
						var vc = GetLocalVoiceChat();
						if (vc != null)
							vc.IsInputMuted = (bool)v;
					})
				// ── Deafened (getter/setter) ──
				.AddVariable("deafened",
					getter: () => {
						var vc = GetLocalVoiceChat();
						return vc != null ? vc.IsDeafened : false;
					},
					setter: v => {
						var vc = GetLocalVoiceChat();
						if (vc != null)
							vc.IsDeafened = (bool)v;
					})
				// ── 3D Spatial settings (methods with arguments) ──
				.AddMethod("setSpatialMinDistance", (ctx, args) => {
					var cfg = GetConfig();
					if (cfg != null && args.Length > 0 && args[0] is double d)
						cfg.SpatialMinDistance = Mathf.Max(0f, (float)d);
					return null;
				})
				.AddMethod("setSpatialMaxDistance", (ctx, args) => {
					var cfg = GetConfig();
					if (cfg != null && args.Length > 0 && args[0] is double d)
						cfg.SpatialMaxDistanceNormal = Mathf.Max(1f, (float)d);
					return null;
				})
				.AddMethod("setWhisperMaxDistance", (ctx, args) => {
					var cfg = GetConfig();
					if (cfg != null && args.Length > 0 && args[0] is double d)
						cfg.SpatialMaxDistanceWhisper = Mathf.Max(0.5f, (float)d);
					return null;
				})
				.Build();

		private static Session GetSession()
			=> Session.Current;

		private static NoxVoiceConfig GetConfig() {
			var session = GetSession();
			if (session == null) return null;
			var localPlayer = session.InterEntities?.LocalPlayer;
			if (localPlayer == null) return null;
			if (localPlayer.TryGetPhysical<Physical>(out var physical))
				return physical.gameObject?.GetComponent<NoxVoiceChat>()?.Config;
			return null;
		}

		private static NoxVoiceChat GetLocalVoiceChat() {
			var session = GetSession();
			var localPlayer = session?.InterEntities?.LocalPlayer;
			if (localPlayer != null && localPlayer.TryGetPhysical<Physical>(out var physical))
				return physical.gameObject?.GetComponent<NoxVoiceChat>();
			return null;
		}

		private static NoxVoiceAudioSourceOutput GetLocalOutput() {
			var vc = GetLocalVoiceChat();
			return vc?.AudioOutput as NoxVoiceAudioSourceOutput;
		}

		private static void ApplyToLocalOutput(VoiceDistanceMode mode) {
			var output = GetLocalOutput();
			if (output != null) {
				output.DistanceMode = mode;
				output.ApplySpatialSettings();
			}
		}
	}
}
