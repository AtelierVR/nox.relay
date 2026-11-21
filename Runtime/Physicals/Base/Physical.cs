using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Nox.Avatars.Parameters;
using Nox.CCK.Development;
using Nox.CCK.Events;
using Nox.CCK.Utils;
using Nox.Entities;
using Nox.Relay.Runtime.Players;
using UnityEngine;
using UnityEngine.Events;
using Gizmos = Nox.CCK.Development.Gizmos;

namespace Nox.Relay.Runtime.Physicals {
	[Gizmos("relay.remote_physical")]
	public abstract class Physical : Nox.Entities.Physical, IGizmos, IPhysicalColliders {
		protected Player Reference { get; set; }

		/// <summary>Colliders of the body, re-read by <see cref="InvalidateColliders"/>.</summary>
		private readonly List<Collider> _colliders = new();

		/// <summary>Scratch list of the last read, compared with <see cref="_colliders"/>.</summary>
		private readonly List<Collider> _readColliders = new();

		/// <inheritdoc />
		public IReadOnlyList<Collider> Colliders
			=> _colliders;

		/// <inheritdoc />
		public UnityEvent<IReadOnlyList<Collider>> OnCollidersChanged { get; } = new();

		/// <summary>
		/// Reads the colliders of the body again and announces them when the set actually changed: the
		/// body gains its colliders at runtime (an avatar is attached, swapped or removed, a collider is
		/// added or removed), so the list is never read once and kept.
		/// </summary>
		public void InvalidateColliders() {
			GetComponentsInChildren(true, _readColliders);

			var same = AreSame(_readColliders, _colliders);

			if (!same) {
				_colliders.Clear();
				_colliders.AddRange(_readColliders);
			}

			_readColliders.Clear();

			if (same)
				return;

			OnCollidersChanged.Invoke(_colliders);
		}

		private static bool AreSame(List<Collider> first, List<Collider> second) {
			if (first.Count != second.Count)
				return false;

			for (var i = 0; i < first.Count; i++)
				if (first[i] != second[i])
					return false;

			return true;
		}

		// DateTime.MaxValue = "no delayed destruction scheduled"
		public DateTime DelayDestroyed { get; private set; } = DateTime.MaxValue;

		public bool IsDestroying
			=> DelayDestroyed != DateTime.MaxValue;

		private CancellationTokenSource _destroyCts;

		public void Destroy(bool immediate = false) {
			if (immediate || !gameObject) {
				_destroyCts?.Cancel();
				gameObject?.DestroyImmediate();
				return;
			}

			// Already scheduling countdown — nothing to do
			if (IsDestroying)
				return;

			// Hide now and schedule actual destruction (cancellable if re-used later)
			_destroyCts?.Cancel();
			_destroyCts = new CancellationTokenSource();
			DelayDestroyed = DateTime.UtcNow.AddSeconds(CCK.Sessions.Settings.ClearPhysicalAfterSeconds);
			if (gameObject.activeSelf)
				gameObject.SetActive(false);
			DestroyAfterDelay(_destroyCts.Token).Forget();
		}

		private async UniTaskVoid DestroyAfterDelay(CancellationToken ct) {
			await UniTask.Delay(
				TimeSpan.FromSeconds(CCK.Sessions.Settings.ClearPhysicalAfterSeconds),
				cancellationToken: ct
			);
			if (ct.IsCancellationRequested || !this || !gameObject)
				return;
			gameObject.Destroy();
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

		private void OnDestroy()
			=> NotifyActuallyDestroyed();

		/// <summary>
		/// Cancels the pending destruction and raises <see cref="ActuallyDestroyed"/>.
		/// <para>
		/// A derived class declaring its own <c>OnDestroy</c> hides this one (Unity only calls the most
		/// derived), so it must call this explicitly for the entity to learn that its physical is gone.
		/// </para>
		/// </summary>
		protected void NotifyActuallyDestroyed() {
			_destroyCts?.Cancel();
			_destroyCts = null;
			ActuallyDestroyed.Invoke();
		}

		virtual protected void OnEnable() {
			_destroyCts?.Cancel();
			DelayDestroyed = DateTime.MaxValue;
			InvalidateColliders();
		}

		virtual protected void OnDisable() {
			if (IsDestroying) return;
			Destroy();
		}

		public void OnDrawGizmos() {
			if (Reference != null) {
				Gizmos.Color = Color.cyan;
				Gizmos.DrawWireSphere(Reference.Position, 0.1f);
				Gizmos.DrawLine(Reference.Position, Reference.Position + Vector3.up * 2f);
			}

			Gizmos.Color = Color.yellow;
			Gizmos.DrawWireCube(transform.position, Vector3.one * 0.2f);
			Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 2f);
		}

        virtual public void OnParameterChanged(IParameter parameter, bool v) { }
    }
}
