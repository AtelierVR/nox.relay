using System;
using Cysharp.Threading.Tasks;
using Nox.Relay.Core.Types.Latency;
using UnityEngine.Events;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Relay.Runtime {
	/// <summary>Adapter binding, connection lifecycle events and latency reporting.</summary>
	public sealed partial class Session {
		public UnityEvent OnConnected { get; } = new();
		public UnityEvent<string> OnDisconnected { get; } = new();

		public UnityEvent<double> OnPingChanged { get; } = new();

		public bool IsConnected
			=> Adapter?.Connector.IsConnected ?? false;

		public DateTime Time
			=> Adapter?.Time ?? DateTime.MinValue;

		public double Ping
			=> Adapter?.Ping ?? -1;

		internal void SetAdapter(Core.Relay adapter) {
			Adapter?.Connector.OnDisconnected.RemoveListener(OnDisconnectedHandler);
			Adapter?.Connector.OnConnected.RemoveListener(OnConnectedHandler);
			Adapter?.OnLatency.RemoveListener(OnLatencyUpdated);
			Adapter?.OnTransfer.RemoveListener(OnTransferHandler);
			Adapter = adapter;
			Adapter.Connector.OnDisconnected.AddListener(OnDisconnectedHandler);
			Adapter.Connector.OnConnected.AddListener(OnConnectedHandler);
			Adapter.OnLatency.AddListener(OnLatencyUpdated);
			Adapter.OnTransfer.AddListener(OnTransferHandler);

			if (Adapter.Connector.IsConnected && !_reconnecting && !_transferring)
				OnConnectedHandler(true);
		}

		private void OnDisconnectedHandler(string reason) {
			OnDisconnected.Invoke(reason);

			if (_disposed)
				return;

			UniTask.Post(() => {
				if (_disposed)
					return;

				NotifyModulesDisconnected(reason);

				// A transfer owns the lifecycle of the next connection: it must not be turned into
				// a reconnection to the relay we are leaving.
				if (!_transferring)
					StartReconnect(reason);
			});
		}

		/// <summary>
		/// Detaches the current adapter and disposes it without letting its shutdown raise the
		/// disconnected handler (which would start another reconnection).
		/// </summary>
		internal async UniTask DetachAdapterAsync() {
			var adapter = Adapter;
			if (adapter == null)
				return;

			Adapter = null;

			adapter.Connector.OnDisconnected.RemoveListener(OnDisconnectedHandler);
			adapter.Connector.OnConnected.RemoveListener(OnConnectedHandler);
			adapter.OnLatency.RemoveListener(OnLatencyUpdated);
			adapter.OnTransfer.RemoveListener(OnTransferHandler);

			try {
				await adapter.Dispose();
			} catch (Exception e) {
				Logger.LogException(e, Tag);
			}
		}

		private void OnConnectedHandler(bool success) {
			if (!success) {
				Logger.LogError("Failed to connect to the server", tag: Tag);
				return;
			}

			OnConnected.Invoke();
			NotifyModulesConnected();
		}

		private void OnLatencyUpdated(LatencyResponse arg0)
			=> OnPingChanged.Invoke(arg0.GetLatency().TotalMilliseconds);
	}
}
