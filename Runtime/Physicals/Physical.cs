using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Nox.CCK.Development;
using Nox.CCK.Events;
using Nox.CCK.Sessions;
using Nox.CCK.Utils;
using Nox.Relay.Runtime.Players;
using UnityEngine;
using Gizmos = Nox.CCK.Development.Gizmos;

namespace Nox.Relay.Runtime.Physicals {
	[Gizmos("relay.remote_physical")]
	public abstract class Physical : Nox.Entities.Physical, IGizmos {
		protected Player Reference { get; set; }

		// DateTime.MaxValue = "no delayed destruction scheduled"
		public DateTime DelayDestroyed { get; private set; } = DateTime.MaxValue;

		public bool IsDestroying => DelayDestroyed != DateTime.MaxValue;

		private CancellationTokenSource _destroyCts;

		public void Destroy(bool immediate = false) {
			if (immediate || !gameObject) {
				_destroyCts?.Cancel();
				gameObject?.Destroy();
				return;
			}

			// Already scheduling countdown — nothing to do
			if (IsDestroying)
				return;

			// Hide now and schedule actual destruction (cancellable if re-used later)
			_destroyCts?.Cancel();
			_destroyCts = new CancellationTokenSource();
			DelayDestroyed = DateTime.UtcNow.AddSeconds(Settings.ClearPhysicalAfterSeconds);
			if (gameObject.activeSelf)
				gameObject.SetActive(false);
			DestroyAfterDelay(_destroyCts.Token).Forget();
		}

		private async UniTaskVoid DestroyAfterDelay(CancellationToken ct) {
			await UniTask.Delay(
				TimeSpan.FromSeconds(Settings.ClearPhysicalAfterSeconds),
				cancellationToken: ct
			);
			if (!ct.IsCancellationRequested)
				gameObject?.Destroy();
		}

		/// <summary>Cancels the pending delayed destruction and re-activates the GameObject.</summary>
		public void CancelDestroy() {
			_destroyCts?.Cancel();
			_destroyCts  = null;
			DelayDestroyed = DateTime.MaxValue;
			gameObject.SetActive(true);
		}

		/// <summary>Fired when the GameObject is actually destroyed (not just hidden).</summary>
		public readonly NoxEvent ActuallyDestroyed = new();

		private void OnDestroy() {
			_destroyCts?.Cancel();
			ActuallyDestroyed.Invoke();
		}

		virtual protected void OnEnable() {
			_destroyCts?.Cancel();
			DelayDestroyed = DateTime.MaxValue;
		}

		virtual protected void OnDisable() {
			if (IsDestroying) return;
			Destroy();
		}

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