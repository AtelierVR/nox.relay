using System;
using System.Net;
using Cysharp.Threading.Tasks;
using UnityEngine.Events;
using Buffer = Nox.CCK.Utils.Buffer;

namespace Nox.Relay.Core.Connectors {
	/// <summary>
	/// Interface for a network connector.
	/// </summary>
	public interface IConnector {
		/// <summary>
		/// Get the name of the protocol used by this connector.
		/// </summary>
		/// <returns></returns>
		string Protocol { get; }

		/// <summary>
		/// Check if the connector is connected.
		/// </summary>
		/// <returns></returns>
		bool IsConnected { get; }

		/// <summary>
		/// Get the local endpoint of the connector.
		/// </summary>
		/// <returns></returns>
		EndPoint EndPoint { get; }

		/// <summary>
		/// Connect to a remote address and port.
		/// </summary>
		/// <param name="address"></param>
		/// <param name="port"></param>
		/// <returns></returns>
		UniTask<bool> Connect(string address, ushort port);

		/// <summary>
		/// Get or set the MTU (Maximum Transmission Unit) of the connector.
		/// </summary>
		ushort Mtu { get; set; }

		/// <summary>
		/// Close the connector.
		/// </summary>
		/// <returns></returns>
		UniTask Close();

		/// <summary>
		/// Event triggered when a buffer is received.
		/// </summary>
		UnityEvent<Buffer> OnReceived { get; }

		/// <summary>
		/// Send a buffer through the connector.
		/// </summary>
		/// <param name="buffer"></param>
		/// <returns></returns>
		UniTask<bool> Send(Buffer buffer);

		/// <summary>
		/// Event triggered when the connector is connected.
		/// </summary>
		UnityEvent OnConnected { get; }

		/// <summary>
		/// Event triggered when the connector is disconnected.
		/// <remarks>The string parameter contains the reason for disconnection.</remarks>
		/// </summary>
		UnityEvent<string> OnDisconnected { get; }
	}
}