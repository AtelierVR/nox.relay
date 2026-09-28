using Nox.Sessions;

namespace Nox.Relay.Runtime {
	public class State : IState {
		public State(Status status, string message, float progress, bool cancelable = false) {
			Message    = message;
			Progress   = progress;
			Status     = status;
			Cancelable = cancelable;
		}

		public string Message    { get; }
		public float  Progress   { get; }
		public Status Status     { get; }
		public bool   Cancelable { get; }
	}
}