using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Nox.Relay.Runtime.Voice {
	/// <summary>
	/// RMS jitter calculator — tracks network jitter to adjust output latency.
	/// </summary>
	public class VoiceJitter {
		private readonly double _timeWindow;
		private readonly int _meanOffsetWindow;

		private readonly System.Diagnostics.Stopwatch _stopwatch = new();
		private double LocalTimestamp => _stopwatch.Elapsed.TotalSeconds;

		private readonly Queue<Entry> _entries = new();
		private readonly Queue<double> _offsets = new();

		public VoiceJitter() {
			_timeWindow = VoiceConfig.JitterTimeWindow;
			_meanOffsetWindow = VoiceConfig.JitterMeanOffsetWindow;
		}

		/// <summary>
		/// Feed a new packet and get the current RMS jitter.
		/// When sender timestamps are unavailable (timestamp=0), uses arrival-timing mode.
		/// </summary>
		public float Update(double timestamp) {
			if (!_stopwatch.IsRunning) {
				_stopwatch.Restart();
				return 0;
			}

			double localTimestamp = LocalTimestamp;
			double effectiveTimestamp = timestamp > 0 ? timestamp : localTimestamp;

			_entries.Enqueue(new Entry(effectiveTimestamp, localTimestamp));
			while (_entries.TryPeek(out var entry)) {
				if (entry.GetAge(localTimestamp) > _timeWindow)
					_entries.Dequeue();
				else
					break;
			}

			_offsets.Enqueue(localTimestamp - effectiveTimestamp);
			if (_offsets.Count > _meanOffsetWindow)
				_offsets.Dequeue();

			double meanOffset = _offsets.Average();

			if (_entries.Count > 1) {
				float SquareDeviation(Entry e) {
					double deviation = meanOffset + e.timestamp - e.localTimestamp;
					return (float)(deviation * deviation);
				}
				return Mathf.Sqrt(_entries.Average(SquareDeviation));
			}

			return 0;
		}

		public void Reset() {
			_stopwatch.Reset();
			_entries.Clear();
			_offsets.Clear();
		}

		private readonly struct Entry {
			public readonly double timestamp;
			public readonly double localTimestamp;

			public Entry(double timestamp, double localTimestamp) {
				this.timestamp = timestamp;
				this.localTimestamp = localTimestamp;
			}

			public float GetAge(double localTimestamp)
				=> (float)(localTimestamp - this.localTimestamp);
		}
	}
}
