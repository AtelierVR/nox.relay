using System;
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
			Adapter = adapter;
			Adapter.Connector.OnDisconnected.AddListener(OnDisconnectedHandler);
			Adapter.Connector.OnConnected.AddListener(OnConnectedHandler);
			Adapter.OnLatency.AddListener(OnLatencyUpdated);
			if (Adapter.Connector.IsConnected)
				OnConnectedHandler(true);
		}

		private void OnDisconnectedHandler(string reason)
			=> OnDisconnected.Invoke(reason);

		private void OnConnectedHandler(bool success) {
			if (!success) {
				Logger.LogError("Failed to connect to the server", tag: Tag);
				return;
			}

			OnConnected.Invoke();
		}

		private void OnLatencyUpdated(LatencyResponse arg0)
			=> OnPingChanged.Invoke(arg0.GetLatency().TotalMilliseconds);
	}
}
