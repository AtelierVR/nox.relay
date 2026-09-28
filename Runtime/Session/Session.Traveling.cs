using System;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Nox.CCK.Utils;
using Nox.CCK.Worlds;
using Nox.Relay.Core.Types.Traveling;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Relay.Runtime {
	/// <summary>World traveling: resolving, downloading and loading the destination world.</summary>
	public sealed partial class Session {
		public async UniTask<bool> OnTravelingAsync(TravelingEvent @event, bool response = true, Action<float, string> progress = null, CancellationToken token = default) {
			token.ThrowIfCancellationRequested();

			string hash;
			string url;

			var password = @event.Password;

			if (@event.UseUrl) {
				progress?.Invoke(0.1f, "Using provided URL for world travel");
				hash = BitConverter.ToString(@event.Hash)
					.Replace("-", "")
					.ToLowerInvariant();
				url = @event.DownloadUrl;
			} else if (@event.UseNode) {
				var identifier = @event.Identifier.ToString(Adapter.LastHandshake.MasterAddress);
				progress?.Invoke(0.1f, "Searching for master asset for world travel");
				Logger.LogDebug($"Searching {identifier}", tag: Tag);
				var travelVersion = @event.Identifier.GetVersion();
				if (travelVersion == WorldIdentifierExtensions.DefaultVersion) {
					var worldData = await Main.WorldAPI.Fetch(Identifier.Parse(identifier), token);
					travelVersion = worldData.Release.Value;
				}
				var req = new AssetSearchRequest {
					Engines   = new[] { EngineExtensions.CurrentEngine.GetEngineName() },
					Platforms = new[] { PlatformExtensions.CurrentPlatform.GetPlatformName() },
					Versions  = new[] { travelVersion },
					Limit     = 1
				};

				var asset = (await Main.WorldAPI.SearchAssets(
						Identifier.Parse(identifier),
						req,
						token
					))?.Items
					.FirstOrDefault();

				if (asset == null) {
					progress?.Invoke(0.2f, $"No master asset found for world {identifier}");
					Logger.LogError($"No asset found for world {identifier}", tag: Tag);
					if (response)
						await OnTravelingFailed(@event, "No master asset found");
					return false;
				}

				hash = asset.Hash;
				url  = asset.Url;
			} else {
				progress?.Invoke(0.1f, "The traveling does not contain valid URL or master asset information");
				Logger.LogError($"{@event} does not contain valid URL or master asset information", tag: Tag);
				if (response)
					await OnTravelingFailed(@event, "Invalid traveling information");
				return false;
			}

			progress?.Invoke(0.2f, "Searching for world");
			if (!Main.WorldAPI.HasInCache(hash)) {
				var download = Main.WorldAPI.DownloadToCache(
					url,
					hash: hash,
					progress: f => progress?.Invoke(0.2f + f * 0.45f, "Downloading world..."),
					token: token
				);
				await download.Start();
				token.ThrowIfCancellationRequested();
			}

			progress?.Invoke(0.65f, "Loading world");
			var scene = await Main.WorldAPI.LoadFromCache(
				hash,
				progress: f => progress?.Invoke(0.65f + f * 0.25f, "Loading world..."),
				token: token
			);
			if (scene == null) {
				progress?.Invoke(0.9f, "Failed to load scene for world");
				Logger.LogError($"Failed to load scene for world {@event.Identifier.ToString()}", tag: Tag);
				if (response)
					await OnTravelingFailed(@event, "Failed to load scene");
				return false;
			}

			scene.Identifier = @event.UseNode
				? @event.Identifier
				: Identifier.Invalid;

			SetDimension(scene);
			progress?.Invoke(1f, "World loaded successfully");
			if (response)
				await OnTravelingSuccess(@event);
			return true;
		}

		public void OnTraveling(TravelingEvent @event)
			=> OnTravelingAsync(@event).Forget();

		private async UniTask OnTravelingFailed(TravelingEvent @event, string reason)
			=> await @event.Room.Traveling(TravelingRequest.Failed(reason));

		private async UniTask OnTravelingSuccess(TravelingEvent @event)
			=> await @event.Room.Traveling(TravelingRequest.Ready());
	}
}
