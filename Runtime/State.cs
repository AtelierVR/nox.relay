using Nox.Sessions;

namespace Nox.Relay.Runtime {
	public class State : IState {
		public State(Status status, string message, float progress) {
			Message  = message;
			Progress = progress;
			Status   = status;
		}

		public string Message  { get; }
		public float  Progress { get; }
		public Status Status   { get; }
	}
}