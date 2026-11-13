using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Nox.CCK.Events;
using Nox.CCK.Utils;
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
			_ids = new int[world.Dimensions.Count()];
			for (var i = 0; i < _ids.Length; i++)
				_ids[i] = UnloadedIndex;
		}

		public async UniTask<bool> CreateIfMissing(int index) {
			if (index < 0 || index >= _ids.Length) {
				Logger.LogWarning($"Tried to create dimension instance at invalid index {index} for world {_world.Identifier}", tag: nameof(Dimensions));
				return false;
			}

			if (IsLoaded(index)) return true;
			if (!TryDimension(index, out var dimension)) {
				Logger.LogWarning($"Tried to create dimension instance at index {index} for world {_world.Identifier}, but dimension not found", tag: nameof(Dimensions));
				return false;
			}

			_ids[index] = await dimension.MakeInstance();
			Logger.LogDebug($"Created dimension instance at index {index}", tag: nameof(Dimensions));
			var descriptor = dimension.GetDescriptor(_ids[index]);
			var anchor = dimension.GetAnchor(_ids[index]);
			anchor.GetOrAddComponent<Anchor>().Add("session", _session.Id);
			RefreshModules();
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
				Logger.LogWarning($"Tried to get dimension at invalid index {index} for world {_world.Identifier}", tag: nameof(Dimensions));
				return false;
			}

			dimension = _world.GetDimension(index);
			if (dimension == null) {
				Logger.LogWarning($"Tried to get dimension at index {index} for world {_world.Identifier}, but dimension not found", tag: nameof(Dimensions));
				return false;
			}

			return true;
		}

		public Identifier Identifier
			=> _world.Identifier;

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
			=> _world.IsCurrent = true;

		public void Dispose() {
			var unloaded = false;
			for (var i = 0; i < _ids.Length; i++) {
				if (_ids[i] == UnloadedIndex) continue;
				if (TryDimension(i, out var instance))
					instance.RemoveInstance(_ids[i]);
				_ids[i] = UnloadedIndex;
				_session.OnSceneUnloaded(i);
				unloaded = true;
			}

			if (unloaded)
				RefreshModules();
		}

		public IWorldDescriptor[] GetDescriptors() {
			var descriptors = new IWorldDescriptor[_ids.Length];
			for (var i = 0; i < _ids.Length; i++)
				descriptors[i] = GetDescriptor(i);
			return descriptors;
		}


		// Session modules


		/// <summary>
		/// Session modules of every loaded dimension, flattened.
		/// <para>
		/// The dimensions own the list because they own the descriptor lifetime, the only thing that
		/// makes the set change, exactly like <c>AvatarDescriptor.Modules</c> does for an avatar:
		/// consumers read the stored array instead of flattening the descriptors on every call, and
		/// subscribe to <see cref="OnModuleRegistered"/> / <see cref="OnModuleUnregistered"/> instead
		/// of polling. The relay session reads it on every player/entity event, and twice per frame.
		/// </para>
		/// </summary>
		public ISessionModule[] Modules { get; private set; } = Array.Empty<ISessionModule>();

		/// <summary>Raised when a session module joins the loaded dimensions.</summary>
		public NoxEvent<ISessionModule> OnModuleRegistered { get; } = new();

		/// <summary>Raised when a session module leaves the loaded dimensions.</summary>
		public NoxEvent<ISessionModule> OnModuleUnregistered { get; } = new();

		/// <summary>
		/// Rebuilds <see cref="Modules"/> and announces the modules that joined or left. Called when
		/// an instance is created or removed, the two points where a descriptor enters or leaves the
		/// session.
		/// </summary>
		/// <returns>The refreshed module list.</returns>
		public ISessionModule[] RefreshModules() {
			var descriptors = GetDescriptors();
			var modules = new List<ISessionModule>();

			for (var i = 0; i < descriptors.Length; i++) {
				var descriptor = descriptors[i];
				if (descriptor == null)
					continue;

				var descriptorModules = descriptor.GetModules();
				for (var j = 0; j < descriptorModules.Length; j++)
					if (descriptorModules[j] is ISessionModule module)
						modules.Add(module);
			}

			var previous = Modules;
			var current = modules.ToArray();
			Modules = current;

			// Diff the two lists: a module added or destroyed at runtime is announced once, and the
			// refresh stays linear (`Array.IndexOf` compares references directly, no hashing).
			for (var i = 0; i < current.Length; i++)
				if (Array.IndexOf(previous, current[i]) < 0)
					OnModuleRegistered.Invoke(current[i]);

			for (var i = 0; i < previous.Length; i++)
				if (Array.IndexOf(current, previous[i]) < 0)
					OnModuleUnregistered.Invoke(previous[i]);

			return current;
		}
	}
}
