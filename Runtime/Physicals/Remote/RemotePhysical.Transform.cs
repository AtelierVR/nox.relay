using System.Collections.Generic;
using Nox.Avatars.Rigging;
using Nox.CCK.Avatars.Rigging;
using Nox.CCK.Entities;
using Nox.Relay.Runtime.Players;
using UnityEngine;

namespace Nox.Relay.Runtime.Physicals {
	/// <summary>
	/// Transform interpolation of the remote player's parts onto this physical.
	/// </summary>
	public partial class RemotePhysical {

		/// <summary>
		/// Bones already declared as driven on the current rig (controller channel). Reset with the avatar,
		/// since the incoming rig has to be declared again.
		/// </summary>
		private readonly HashSet<HumanBodyBones> _drivenBones = new();

		/// <summary>Rig the bones above were declared on: a hot-swap must declare them again.</summary>
		private IRigging _declaredRig;

		private void Update() {
			UpdateNameplate();

			if (Reference == null) return;

			var activeRig = _rigProvider?.Rig;

			// The rig may be created after the avatar (or replaced by a backend hot-swap): the controller
			// channel has to be declared again on the new one.
			if (!ReferenceEquals(activeRig, _declaredRig)) {
				_declaredRig = activeRig;
				_drivenBones.Clear();
			}

			var now = Time.timeAsDouble;
			var dt = Time.deltaTime;
			var tps = Reference.Reference.Room?.Tps ?? 0;
			var nominal = tps > 0 ? 1f / tps : 0f;

			foreach (var (partId, part) in Reference.Parts) {
				if (part is not RemotePart remote)
					continue;

				var rig = partId.ToEntityRig();

				if (activeRig != null && IsTrackerDriven(rig) && _drivenBones.Add(rig.ToHumanBodyBones()))
					activeRig.SetActive(rig.ToHumanBodyBones(), true);

				if (!remote.Buffer.TrySample(now, nominal, dt, out var pose))
					continue;

				if (rig == EntityRig.Base) {
					transform.position = pose.Position;
					transform.rotation = pose.Rotation;
					transform.localScale = pose.Scale;
				} else if (activeRig != null)
					RigPartDriver.Write(activeRig, partId, pose.Position, pose.Rotation);
			}
		}

		/// <summary>
		/// Bones a player drives from trackers, i.e. the ones a controller declares with <c>IRigging.SetActive</c>:
		/// the body chain (pelvis, spine, chest, neck), the arms with the hands and the legs/feet/toes.
		/// <c>Head</c> is left out on purpose - it is always driven and the backend already has it active, so
		/// declaring it would only override the avatar's own choice.
		/// </summary>
		private static bool IsTrackerDriven(EntityRig rig) {
			var id = rig.ToIndex();
			return (id >= EntityRig.Hips.ToIndex() && id <= EntityRig.Neck.ToIndex())
				   || (id >= EntityRig.LeftShoulder.ToIndex() && id <= EntityRig.RightHand.ToIndex())
				   || (id >= EntityRig.LeftUpperLeg.ToIndex() && id <= EntityRig.RightToes.ToIndex());
		}

	}
}
