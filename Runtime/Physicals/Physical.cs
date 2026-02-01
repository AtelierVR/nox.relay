using System;
using Nox.CCK.Development;
using Nox.CCK.Sessions;
using Nox.CCK.Utils;
using Nox.Relay.Runtime.Players;
using UnityEngine;
using Gizmos = Nox.CCK.Development.Gizmos;

namespace Nox.Relay.Runtime.Physicals {
	[Gizmos("relay.remote_physical")]
	public abstract class Physical : Nox.Entities.Physical, IGizmos {
		protected Player Reference { get; set; }

		public DateTime DelayDestroyed { get; private set; } = DateTime.MinValue;

		public void Destroy(bool immediate = false) {
			if (immediate || !gameObject || DelayDestroyed <= DateTime.UtcNow) {
				gameObject?.Destroy();
				return;
			}

			if (!gameObject.activeSelf)
				return;

			DelayDestroyed = DateTime.UtcNow.AddSeconds(Settings.ClearPhysicalAfterSeconds);
			gameObject.SetActive(false);
		}

		private void OnDestroy()
			=> Destroy(true);

		virtual protected void OnEnable()
			=> DelayDestroyed = DateTime.MinValue;

		public void OnDrawGizmos() {
			if (Reference != null) {
				Gizmos.color = Color.cyan;
				Gizmos.DrawWireSphere(Reference.Position, 0.1f);
				Gizmos.DrawLine(Reference.Position, Reference.Position + Vector3.up * 2f);
			}

			Gizmos.color = Color.yellow;
			Gizmos.DrawWireCube(transform.position, Vector3.one * 0.2f);
			Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 2f);
		}
	}
}