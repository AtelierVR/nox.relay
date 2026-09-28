using Nox.CCK.Sessions;
using UnityEngine;

namespace Nox.Relay.Runtime {
	/// <summary>Physics environment owned by the session (simulation + gravity).</summary>
	public sealed partial class Session {
		/// <inheritdoc cref="IPhysicalSession.Simulation"/>
		public bool Simulation {
			get => this.GetSimulation();
			set {
				this.SetSimulation(value);

				// Applies to the engine only when this session is the current one.
				if (SessionHelper.IsCurrent(Main.SessionAPI, this))
					Physics.simulationMode = value
						? SimulationMode.Update
						: SimulationMode.Script;
			}
		}

		/// <inheritdoc cref="IPhysicalSession.Gravity"/>
		public Vector3 Gravity {
			get => this.GetGravity();
			set {
				this.SetGravity(value);

				// Applies to the engine only when this session is the current one.
				if (SessionHelper.IsCurrent(Main.SessionAPI, this))
					Physics.gravity = value;
			}
		}
	}
}
