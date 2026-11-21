using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Nox.CCK.Utils;
using Nox.Entities;
using Nox.Relay.Core.Rooms;
using Nox.Relay.Core.Types.Properties;
using Nox.Relay.Core.Types.Transform;
using Nox.Sessions;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Relay.Runtime {
	/// <summary>
	/// Streams the state of the entities the local player owns, itself included: their parts and their
	/// properties are sent the same way whatever the entity is (a player rig, a prop, an entity a mod
	/// built) â€” a delta while the values move, plus a periodic keyframe so a peer that missed an
	/// update still receives the complete state.
	/// <para>
	/// The session does it for every entity instead of each entity doing it for itself, so an entity
	/// only has to expose its parts and its properties.
	/// </para>
	/// </summary>
	public sealed partial class Session {
		/// <summary>What was already sent for one entity, and the scratch buffers of the tick.</summary>
		private sealed class StreamState {
			internal readonly Dictionary<ushort, PartStream> Parts = new();
			internal readonly List<ushort> DirtyParts = new();
			internal readonly List<IProperty> DirtyProperties = new();
			internal readonly List<UniTask<bool>> Tasks = new();
			internal readonly List<PropertiesRequest> Requests = new();
			internal bool SendingParts;
			internal bool SendingProperties;
		}

		/// <summary>Cached pose of one part: the last values sent, plus the scratch delta of the tick.</summary>
		private sealed class PartStream {
			internal readonly TransformObject Sent = new();
			internal readonly TransformObject Exported = new();
			internal readonly TransformObject Delta = new();
			internal DateTime SentAt = DateTime.MinValue;
		}

		private readonly Dictionary<uint, StreamState> _streams = new();

		private StreamState StreamOf(uint id) {
			if (!_streams.TryGetValue(id, out var state))
				_streams[id] = state = new StreamState();

			return state;
		}

		/// <summary>Forget the cached pose of a part of an entity: the next send is complete.</summary>
		internal void ResetPartCache(uint entityId, ushort part) {
			if (_streams.TryGetValue(entityId, out var state))
				state.Parts.Remove(part);
		}

		/// <summary>Forget the stream state of an entity that left.</summary>
		private void DropStream(uint entityId)
			=> _streams.Remove(entityId);

		/// <summary>
		/// Send the parts and the properties of every entity the local player owns (the local player
		/// is one of them: it owns itself). Called once per session tick.
		/// </summary>
		private void SendOwnedStreams() {
			foreach (var entity in _entities.GetEntities())
				if (entity is ISessionEntity { IsNetworked: true } session && session.Owner is { IsLocal: true })
					StreamEntity(entity, session);
		}

		private void StreamEntity(IEntity entity, ISessionEntity session) {
			var state = StreamOf(entity.Id);

			SendParts(entity, state).Forget();
			SendProperties(entity, session, state).Forget();
		}

		/// <summary>
		/// Send the parts that moved beyond the threshold of the room, plus a periodic keyframe so a
		/// peer that missed an update gets the complete pose.
		/// </summary>
		private async UniTask SendParts(IEntity entity, StreamState state) {
			if (state.SendingParts)
				return;

			state.SendingParts = true;
			try {
				var room = Room;
				if (room == null || entity is not IMultiPartEntity<IPart> multi)
					return;

				var parts = multi.GetParts();
				if (parts.Length == 0)
					return;

				var threshold = room.Threshold < 0.0001f
					? 0.0001f
					: room.Threshold;
				var keyframe = room.PropertyResendInterval;
				var now = DateTime.UtcNow;

				state.DirtyParts.Clear();

				foreach (var part in parts) {
					if (part == null)
						continue;

					if (!state.Parts.TryGetValue(part.Id, out var stream))
						state.Parts[part.Id] = stream = new PartStream();

					var delta = stream.Delta;
					delta.Clear();

					var exported = stream.Exported;
					part.Export(exported);

					var changed = false;

					if (exported.Flags.HasFlag(TransformFlags.Position) && !stream.Sent.IsSamePosition(exported.GetPosition(), threshold)) {
						delta.SetPosition(exported.GetPosition());
						stream.Sent.SetPosition(exported.GetPosition());
						changed = true;
					}

					if (exported.Flags.HasFlag(TransformFlags.Rotation) && !stream.Sent.IsSameRotation(exported.GetRotation(), threshold)) {
						delta.SetRotation(exported.GetRotation());
						stream.Sent.SetRotation(exported.GetRotation());
						changed = true;
					}

					if (exported.Flags.HasFlag(TransformFlags.Scale) && !stream.Sent.IsSameScale(exported.GetScale(), threshold)) {
						delta.SetScale(exported.GetScale());
						stream.Sent.SetScale(exported.GetScale());
						changed = true;
					}

					if (exported.Flags.HasFlag(TransformFlags.Velocity) && !stream.Sent.IsSameVelocity(exported.GetVelocity(), threshold)) {
						delta.SetVelocity(exported.GetVelocity());
						stream.Sent.SetVelocity(exported.GetVelocity());
						changed = true;
					}

					if (exported.Flags.HasFlag(TransformFlags.Angular) && !stream.Sent.IsSameAngular(exported.GetAngular(), threshold)) {
						delta.SetAngular(exported.GetAngular());
						stream.Sent.SetAngular(exported.GetAngular());
						changed = true;
					}

					var overdue = keyframe > 0 && (now - stream.SentAt).TotalSeconds >= keyframe;

					if (!changed && !overdue)
						continue;

					if (overdue && !changed) {
						if (exported.Flags.HasFlag(TransformFlags.Position))
							delta.SetPosition(exported.GetPosition());
						if (exported.Flags.HasFlag(TransformFlags.Rotation))
							delta.SetRotation(exported.GetRotation());
						if (exported.Flags.HasFlag(TransformFlags.Scale))
							delta.SetScale(exported.GetScale());
						if (exported.Flags.HasFlag(TransformFlags.Velocity))
							delta.SetVelocity(exported.GetVelocity());
						if (exported.Flags.HasFlag(TransformFlags.Angular))
							delta.SetAngular(exported.GetAngular());
					}

					stream.SentAt = now;
					state.DirtyParts.Add(part.Id);
				}

				if (state.DirtyParts.Count > 0)
					await SendPartsBatch(room, entity.Id, state);
			} finally {
				state.SendingParts = false;
			}
		}

		private async UniTask SendPartsBatch(Room room, uint entityId, StreamState state) {
			state.Tasks.Clear();

			var time = (uint)(UnityEngine.Time.timeAsDouble * 1000.0);

			foreach (var part in state.DirtyParts) {
				var request = TransformRequest.CreateEntity(entityId, part, state.Parts[part].Delta);
				request.SenderTime = time;
				state.Tasks.Add(room.Transform(request));
			}

			if (state.Tasks.Count == 1)
				await state.Tasks[0];
			else
				await UniTask.WhenAll(state.Tasks);

			state.Tasks.Clear();
		}

		/// <summary>
		/// Send the properties that are dirty (a value written since the last send), plus a periodic
		/// keyframe so a peer that joined or missed an update still receives the value.
		/// </summary>
		private async UniTask SendProperties(IEntity entity, IPropertiesEntity properties, StreamState state) {
			if (state.SendingProperties)
				return;

			state.SendingProperties = true;
			try {
				var room = Room;
				if (room == null)
					return;

				var resendSeconds = room.PropertyResendInterval;
				var resendEnabled = resendSeconds > 0;
				var resendInterval = TimeSpan.FromSeconds(resendSeconds);
				var now = DateTime.UtcNow;

				state.DirtyProperties.Clear();

				foreach (var property in properties.Properties) {
					if (!property.Flags.HasFlag(PropertyFlags.LocalEmit))
						continue;

					// A source that announces its writes is already up to date (see
					// IParameter.OnValueChanged); the others are pulled here.
					if (property is IObservableProperty { IsObservable: false } observable)
						observable.Refresh();

					var overdue = resendEnabled
						&& property.Flags.HasFlag(PropertyFlags.Keyframe)
						&& (now - property.UpdatedAt) >= resendInterval;
					if (!property.IsDirty && !overdue)
						continue;

					state.DirtyProperties.Add(property);
				}

				if (state.DirtyProperties.Count > 0)
					await SendPropertiesBatch(room, entity.Id, properties, state);
			} finally {
				state.SendingProperties = false;
			}
		}

		/// <summary>
		/// Send a batch of property updates, split in chunks below the parameter limit of the relay.
		/// </summary>
		private async UniTask SendPropertiesBatch(Room room, uint entityId, IPropertiesEntity properties, StreamState state) {
			state.Tasks.Clear();
			state.Requests.Clear();

			for (var i = 0; i < state.DirtyProperties.Count; i += PropertiesRequest.MaxParameters) {
				var count = Math.Min(PropertiesRequest.MaxParameters, state.DirtyProperties.Count - i);
				var request = PropertiesRequest.Create(entityId, state.DirtyProperties, i, count);
				state.Requests.Add(request);
				state.Tasks.Add(room.Properties(request));
			}

			var results = await UniTask.WhenAll(state.Tasks);

			if (!results.All(r => r)) {
				Logger.LogWarning($"Failed to send some properties for entity {entityId}, will retry on next tick.", tag: Tag);
				return;
			}

			// The payloads were captured when the requests were built: a value written while the packet
			// was in flight was not part of it. Only the properties that still carry the sent payload
			// are marked as delivered, otherwise the newer value would be reported as sent and would
			// never reach the other clients.
			foreach (var request in state.Requests)
				foreach (var entry in request.Parameters) {
					if (!properties.TryGetProperty(entry.Key, out var property) || property == null)
						continue;

					if (!IsSamePayload(property.Serialize(), entry.Value))
						continue;

					property.IsDirty = false;

					if (property is IObservableProperty observable)
						observable.UpdateCache();
				}

			state.Requests.Clear();
			state.Tasks.Clear();
		}

		private static bool IsSamePayload(byte[] sent, byte[] current) {
			if (ReferenceEquals(sent, current))
				return true;

			if (sent == null || current == null || sent.Length != current.Length)
				return false;

			for (var i = 0; i < sent.Length; i++)
				if (sent[i] != current[i])
					return false;

			return true;
		}
	}
}
