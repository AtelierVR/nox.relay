using System.Collections.Generic;
using Nox.Avatars.Rigging;
using Nox.CCK.Avatars.Rigging;
using Nox.CCK.Players;
using UnityEngine;

namespace Nox.Relay.Runtime.Physicals {
	/// <summary>
	/// Transform interpolation of the remote player's parts onto this physical.
	/// </summary>
	public partial class RemotePhysical {

		// État d'interpolation par part (keyed by partId)
		private struct PartInterpolationState {
			public Vector3    StartPosition;
			public Vector3    TargetPosition;
			public Quaternion StartRotation;
			public Quaternion TargetRotation;
			public Vector3    StartScale;
			public Vector3    TargetScale;
			public float      PosTime;
			public float      RotTime;
			public float      ScaleTime;
		}

		private readonly Dictionary<ushort, PartInterpolationState> _partStates = new();

		/// <summary>
		/// Bones already declared as driven on the current rig (controller channel). Reset with the avatar,
		/// since the incoming rig has to be declared again.
		/// </summary>
		private readonly HashSet<HumanBodyBones> _drivenBones = new();

		private float _tickInterval;

		private static float Smoothstep(float t) => t * t * (3f - 2f * t);

		private void Update() {
			UpdateNameplate();

			if (Reference == null) return;

			// The rig provider is a stable MonoBehaviour that exposes the current IRigging.
			// It is resolved once in SetAvatar; the rig itself may be hot-swapped underneath.
			var activeRig = _rigProvider?.GetRig();

			var dt        = Time.deltaTime;
			var tps       = Reference.Reference.Room.Tps;
			var threshold = Reference.Reference.Room.Threshold;
			_tickInterval = tps > 0 ? 1f / tps : 0.05f;

			foreach (var (partId, part) in Reference.Parts) {
				var rig = partId.ToPlayerRig();

				// A part that arrives is a bone the owner drives from a tracker: declaring it here - the controller
				// channel, exactly what the owner's FullBodyCalibration does before writing its target - is what makes
				// the backend follow the target written below. Without it the bone keeps its animation: the VRIK pelvis
				// weight stays 0 until a hips tracker is declared, so the replayed pelvis stayed ~0.5 m away from the
				// received target while the owner's own was on it.
				if (activeRig != null && IsTrackerDriven(rig) && _drivenBones.Add(rig.ToHumanBodyBones()))
					activeRig.SetActive(rig.ToHumanBodyBones(), true);

				var newTargetPos = part.Position;
				var newTargetRot = part.Rotation;
				var newTargetSca = part.Scale;

				if (!_partStates.TryGetValue(partId, out var state)) {
					// First time: immediate snap, no interpolation.
					state = new PartInterpolationState {
						StartPosition  = newTargetPos,
						TargetPosition = newTargetPos,
						StartRotation  = newTargetRot,
						TargetRotation = newTargetRot,
						StartScale     = newTargetSca,
						TargetScale    = newTargetSca,
						PosTime        = _tickInterval,
						RotTime        = _tickInterval,
						ScaleTime      = _tickInterval,
					};
				} else {
					if (Vector3.Distance(newTargetPos, state.TargetPosition) > threshold) {
						state.StartPosition  = rig == PlayerRig.Base ? transform.position : state.TargetPosition;
						state.TargetPosition = newTargetPos;
						state.PosTime        = 0f;
					}
					if (Quaternion.Angle(newTargetRot, state.TargetRotation) > threshold) {
						state.StartRotation  = rig == PlayerRig.Base ? transform.rotation : state.TargetRotation;
						state.TargetRotation = newTargetRot;
						state.RotTime        = 0f;
					}
					if (rig == PlayerRig.Base && Vector3.Distance(newTargetSca, state.TargetScale) > threshold) {
						state.StartScale  = transform.localScale;
						state.TargetScale = newTargetSca;
						state.ScaleTime   = 0f;
					}
				}

				state.PosTime   += dt;
				state.RotTime   += dt;
				state.ScaleTime += dt;

				var tPos   = Smoothstep(Mathf.Clamp01(state.PosTime   / _tickInterval));
				var tRot   = Smoothstep(Mathf.Clamp01(state.RotTime   / _tickInterval));
				var tScale = Smoothstep(Mathf.Clamp01(state.ScaleTime / _tickInterval));

				if (rig == PlayerRig.Base) {
					// Position
					if (Vector3.Distance(state.StartPosition, state.TargetPosition) > threshold * 0.1f) {
						transform.position = Vector3.Lerp(state.StartPosition, state.TargetPosition, tPos);
						if (dt > 0)
							rigidbody.linearVelocity = (state.TargetPosition - transform.position) / _tickInterval;
					} else {
						transform.position       = state.TargetPosition;
						rigidbody.linearVelocity = part.Velocity;
					}

					// Rotation
					if (Quaternion.Angle(state.StartRotation, state.TargetRotation) > threshold * 0.1f) {
						transform.rotation = Quaternion.Slerp(state.StartRotation, state.TargetRotation, tRot);
						var deltaRot = state.TargetRotation * Quaternion.Inverse(transform.rotation);
						deltaRot.ToAngleAxis(out var angle, out var axis);
						if (angle > 180f) angle -= 360f;
						if (_tickInterval > 0)
							rigidbody.angularVelocity = axis * (angle * Mathf.Deg2Rad / _tickInterval);
					} else {
						transform.rotation        = state.TargetRotation;
						rigidbody.angularVelocity = part.Angular;
					}

					// Scale
					transform.localScale = Vector3.Distance(state.StartScale, state.TargetScale) > threshold * 0.1f
						? Vector3.Lerp(state.StartScale, state.TargetScale, tScale)
						: state.TargetScale;
				} else if (activeRig != null) {
					var interpolatedPos = Vector3.Distance(state.StartPosition, state.TargetPosition) > threshold * 0.1f
						? Vector3.Lerp(state.StartPosition, state.TargetPosition, tPos)
						: state.TargetPosition;
					var interpolatedRot = Quaternion.Angle(state.StartRotation, state.TargetRotation) > threshold * 0.1f
						? Quaternion.Slerp(state.StartRotation, state.TargetRotation, tRot)
						: state.TargetRotation;

					// Same helper as the local driver (`AvatarSyncConnector.DriveRigParts`): a part lands on
					// the same rig object on both sides, only the timing differs (interpolated here).
					RigPartDriver.Write(activeRig, partId, interpolatedPos, interpolatedRot);
				}

				_partStates[partId] = state;
			}
		}

		/// <summary>
		/// Bones a player drives from trackers, i.e. the ones a controller declares with <c>IRigging.SetActive</c>:
		/// the body chain (pelvis, spine, chest, neck), the arms with the hands and the legs/feet/toes.
		/// <c>Head</c> is left out on purpose - it is always driven and the backend already has it active, so
		/// declaring it would only override the avatar's own choice.
		/// </summary>
		private static bool IsTrackerDriven(PlayerRig rig) {
			var id = (ushort)rig;
			return (id >= (ushort)PlayerRig.Hips && id <= (ushort)PlayerRig.Neck)
			       || (id >= (ushort)PlayerRig.LeftShoulder && id <= (ushort)PlayerRig.RightHand)
			       || (id >= (ushort)PlayerRig.LeftUpperLeg && id <= (ushort)PlayerRig.RightToes);
		}

	}
}
