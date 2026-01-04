using Cysharp.Threading.Tasks;
using Nox.Sessions;
using Nox.Worlds;
using UnityEngine;
using UnityEngine.SceneManagement;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Relay.Runtime {
	public class Dimensions : IDimensions {
		private const int UnloadedIndex = -1;
		public const int MainIndex = 0;

		private readonly IRuntimeWorld _world;
		private readonly Session _session;
		private readonly int[] _ids;

		internal Dimensions(Session session, IRuntimeWorld world) {
			_world = world;
			_session = session;
			_ids = new int[world.GetDimensionCount()];
			for (var i = 0; i < _ids.Length; i++)
				_ids[i] = UnloadedIndex;
		}

		public async UniTask<bool> CreateIfMissing(int index) {
			if (index < 0 || index >= _ids.Length) {
				Logger.LogWarning($"Tried to create dimension instance at invalid index {index} for world {_world.GetIdentifier()}", tag: nameof(Dimensions));
				return false;
			}

			if (IsLoaded(index)) return true;
			if (!TryDimension(index, out var dimension)) {
				Logger.LogWarning($"Tried to create dimension instance at index {index} for world {_world.GetIdentifier()}, but dimension not found", tag: nameof(Dimensions));
				return false;
			}

			_ids[index] = await dimension.MakeInstance();
			Logger.LogDebug($"Created dimension instance at index {index}", tag: nameof(Dimensions));
			var descriptor = dimension.GetDescriptor(_ids[index]);
			var anchor = dimension.GetAnchor(_ids[index]);
			_session.OnSceneLoaded(index, descriptor, anchor);
			return true;
		}

		public void SetActive(int index, bool isActive) {
			if (!TryDimension(index, out var instance)) return;
			instance.SetVisibleInstance(_ids[index], isActive, isActive);
		}

		private bool TryDimension(int index, out IRuntimeWorldDimension dimension) {
			dimension = null;
			if (index < 0 || index >= _ids.Length) {
				Logger.LogWarning($"Tried to get dimension at invalid index {index} for world {_world.GetIdentifier()}", tag: nameof(Dimensions));
				return false;
			}

			dimension = _world.GetDimension(index);
			if (dimension == null) {
				Logger.LogWarning($"Tried to get dimension at index {index} for world {_world.GetIdentifier()}, but dimension not found", tag: nameof(Dimensions));
				return false;
			}

			return true;
		}

		public IWorldIdentifier Identifier
			=> _world.GetIdentifier();

		public bool IsLoaded(int index)
			=> index >= 0 && index < _ids.Length && _ids[index] != UnloadedIndex;

		public IWorldDescriptor GetDescriptor(int index)
			=> TryDimension(index, out var dimension)
				? dimension.GetDescriptor(_ids[index])
				: null;

		public GameObject GetAnchor(int index)
			=> TryDimension(index, out var dimension)
				? dimension.GetAnchor(_ids[index])
				: null;

		public Scene GetScene(int index)
			=> TryDimension(index, out var dimension)
				? dimension.GetScene()
				: default;

		public void SetCurrent()
			=> _world.SetCurrent();

		public void Dispose() {
			for (var i = 0; i < _ids.Length; i++) {
				if (_ids[i] == UnloadedIndex) continue;
				if (TryDimension(i, out var instance))
					instance.RemoveInstance(_ids[i]);
				_ids[i] = UnloadedIndex;
				_session.OnSceneUnloaded(i);
			}
		}

		public IWorldDescriptor[] GetDescriptors() {
			var descriptors = new IWorldDescriptor[_ids.Length];
			for (var i = 0; i < _ids.Length; i++)
				descriptors[i] = GetDescriptor(i);
			return descriptors;
		}
	}
}