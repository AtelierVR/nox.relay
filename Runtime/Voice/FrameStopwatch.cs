using UnityEngine;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>Simple frame stopwatch for tracking codec time across calls.</summary>
	internal class FrameStopwatch {
		private readonly System.Diagnostics.Stopwatch _sw = new();

		public void Start() => _sw.Restart();

		public void Stop(float maxMs, string message, bool isFirstFrame, bool allowMultiple) {
			_sw.Stop();
			if ((isFirstFrame || allowMultiple) && _sw.Elapsed.TotalMilliseconds > maxMs)
				Debug.LogWarning(message);
		}

		public void Reset() => _sw.Reset();
	}
}
