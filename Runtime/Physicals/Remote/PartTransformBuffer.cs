using UnityEngine;

namespace Nox.Relay.Runtime.Physicals {
	internal struct PartPose {
		public Vector3    Position;
		public Quaternion Rotation;
		public Vector3    Scale;
		public Vector3    LinearVelocity;
		public Vector3    AngularVelocity;
	}

	internal sealed class PartTransformBuffer {
		private struct Snapshot {
			public double     Time;
			public Vector3    Position;
			public Quaternion Rotation;
			public Vector3    Scale;
			public Vector3    Velocity;
			public Vector3    Angular;
		}

		private const int   Capacity             = 32;
		private const float DefaultInterval      = 1f / 60f;
		private const float DelayFactor          = 1.25f;
		private const float MinDelay             = 0.005f;
		private const float MaxDelay             = 0.75f;
		private const float MaxExtrapolation     = 0.1f;
		private const float ExtrapolationFactor  = 0.75f;
		private const float TeleportDistance     = 3f;
		private const float MaxInterpolatedSpeed = 40f;
		private const float CadenceMinFactor     = 0.4f;
		private const float CadenceMaxFactor     = 4f;
		private const float MinSegmentFactor     = 0.25f;
		private const float CadenceSmoothing     = 0.15f;
		private const float JitterSmoothing      = 0.1f;
		private const float SmoothingFactor      = 0.15f;
		private const float ClockSnapThreshold   = 0.5f;
		private const float ClockCorrection      = 2f;

		private readonly Snapshot[] _snapshots = new Snapshot[Capacity];

		private int      _head;
		private int      _count;
		private float    _interval;
		private float    _jitter;
		private double   _renderTime;
		private bool     _hasRenderTime;
		private bool     _hasApplied;
		private PartPose _applied;

		public bool IsEmpty
			=> _count == 0;

		public void Clear() {
			_head          = 0;
			_count         = 0;
			_hasRenderTime = false;
			_hasApplied    = false;
		}

		public void Push(
			double     time,
			float      nominal,
			Vector3    position,
			Quaternion rotation,
			Vector3    scale,
			Vector3    velocity,
			Vector3    angular
		) {
			if (_count > 0) {
				var last = At(_count - 1);
				var dt   = (float)(time - last.Time);

				var cadence  = _interval > 0f ? _interval : Nominal(nominal);
				var span     = Mathf.Max(dt, cadence);
				var distance = Vector3.Distance(position, last.Position);

				if (distance > TeleportDistance || distance / span > MaxInterpolatedSpeed) {
					Clear();
				} else if (dt < cadence * MinSegmentFactor) {
					_snapshots[(_head + _count - 1) % Capacity] = new Snapshot {
						Time     = time > last.Time ? time : last.Time,
						Position = position,
						Rotation = rotation,
						Scale    = scale,
						Velocity = velocity,
						Angular  = angular,
					};
					return;
				} else {
					UpdateCadence(dt, nominal);
				}
			}

			if (_count == Capacity) {
				_head = (_head + 1) % Capacity;
				_count--;
			}

			_snapshots[(_head + _count) % Capacity] = new Snapshot {
				Time     = time,
				Position = position,
				Rotation = rotation,
				Scale    = scale,
				Velocity = velocity,
				Angular  = angular,
			};
			_count++;
		}

		public bool TrySample(double now, float nominal, float deltaTime, out PartPose pose) {
			pose = default;

			if (_count == 0)
				return false;

			var interval = _interval > 0f ? _interval : Nominal(nominal);
			var minimum  = Mathf.Max(MinDelay, interval);
			var delay    = Mathf.Clamp(interval * DelayFactor + _jitter, minimum, Mathf.Max(minimum, MaxDelay));

			UpdateRenderTime(now - delay, deltaTime);
			Prune();

			var a = At(0);
			if (_count == 1 || _renderTime <= a.Time) {
				pose.Position        = a.Position;
				pose.Rotation        = a.Rotation;
				pose.Scale           = a.Scale;
				pose.LinearVelocity  = a.Velocity;
				pose.AngularVelocity = a.Angular;
			} else {
				var b    = At(1);
				var span = b.Time - a.Time;

				if (_renderTime < b.Time) {
					var k = span > 1e-6 ? (float)((_renderTime - a.Time) / span) : 1f;

					pose.Position = Vector3.LerpUnclamped(a.Position, b.Position, k);
					pose.Rotation = Quaternion.SlerpUnclamped(a.Rotation, b.Rotation, k);
					pose.Scale    = Vector3.LerpUnclamped(a.Scale, b.Scale, k);

					if (span > 1e-6) {
						pose.LinearVelocity  = (b.Position - a.Position) / (float)span;
						pose.AngularVelocity = AngularVelocity(a.Rotation, b.Rotation, (float)span);
					}
				} else {
					var ahead    = Mathf.Min((float)(_renderTime - b.Time), Mathf.Min(interval * ExtrapolationFactor, MaxExtrapolation));
					var velocity = span > 1e-6 ? (b.Position - a.Position) / (float)span : Vector3.zero;

					pose.Position       = b.Position + velocity * ahead;
					pose.Rotation       = b.Rotation;
					pose.Scale          = b.Scale;
					pose.LinearVelocity = velocity;
				}
			}

			Smooth(interval, deltaTime, ref pose);

			_applied    = pose;
			_hasApplied = true;
			return true;
		}

		private Snapshot At(int index)
			=> _snapshots[(_head + index) % Capacity];

		private static float Nominal(float nominal)
			=> nominal > 0f ? nominal : DefaultInterval;

		private void UpdateCadence(float dt, float nominal) {
			nominal = Nominal(nominal);

			if (_interval <= 0f)
				_interval = nominal;

			if (dt < nominal * CadenceMinFactor || dt > nominal * CadenceMaxFactor)
				return;

			_interval += (dt - _interval) * CadenceSmoothing;

			var deviation = Mathf.Abs(dt - _interval);
			_jitter = Mathf.Min(_jitter + (deviation - _jitter) * JitterSmoothing, _interval * 0.5f);
		}

		private void UpdateRenderTime(double target, float deltaTime) {
			if (!_hasRenderTime || Mathf.Abs((float)(target - _renderTime)) > ClockSnapThreshold) {
				_renderTime    = target;
				_hasRenderTime = true;
				_hasApplied    = false;
				return;
			}

			_renderTime += deltaTime;
			_renderTime += (target - _renderTime) * Mathf.Clamp01(ClockCorrection * deltaTime);
		}

		private void Prune() {
			while (_count > 2 && At(1).Time <= _renderTime) {
				_head = (_head + 1) % Capacity;
				_count--;
			}
		}

		private void Smooth(float interval, float deltaTime, ref PartPose pose) {
			if (!_hasApplied || deltaTime <= 0f)
				return;

			var tau = interval * SmoothingFactor;
			if (tau <= 1e-4f)
				return;

			if (Vector3.Distance(_applied.Position, pose.Position) > TeleportDistance)
				return;

			var f = 1f - Mathf.Exp(-deltaTime / tau);

			pose.Position = Vector3.Lerp(_applied.Position, pose.Position, f);
			pose.Rotation = Quaternion.Slerp(_applied.Rotation, pose.Rotation, f);
			pose.Scale    = Vector3.Lerp(_applied.Scale, pose.Scale, f);
		}

		private static Vector3 AngularVelocity(Quaternion from, Quaternion to, float span) {
			var delta = to * Quaternion.Inverse(from);
			if (delta.w < 0f)
				delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);

			delta.ToAngleAxis(out var angle, out var axis);
			if (angle <= 1e-4f || float.IsNaN(axis.x) || float.IsNaN(axis.y) || float.IsNaN(axis.z) || axis.sqrMagnitude < 1e-8f)
				return Vector3.zero;

			return axis * (angle * Mathf.Deg2Rad / span);
		}
	}
}
