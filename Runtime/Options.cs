using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Nox.CCK.Utils;
using Nox.CCK.Worlds;
using Nox.Instances;
using UnityEngine;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Relay.Runtime {
	public class Options {
		public static Options From(Dictionary<string, object> dict) {
			var options = new Options();
			if (dict == null) return options;

			if (dict.TryGetValue("title", out var titleObj) && titleObj is string titleStr)
				options.Title = titleStr;
			if (dict.TryGetValue("thumbnail", out var thumbnailObj) && thumbnailObj is Texture2D thumbnailTex)
				options.Thumbnail = thumbnailTex;
			if (dict.TryGetValue("dispose_on_change", out var disposeObj) && disposeObj is bool disposeBool)
				options.DisposeOnChange = disposeBool;
			if (dict.TryGetValue("short_name", out var shortNameObj) && shortNameObj is string shortNameStr)
				options.ShortName = shortNameStr;
			if (dict.TryGetValue("change_current", out var changeCurrentObj) && changeCurrentObj is bool changeCurrentBool)
				options.ChangeCurrent = changeCurrentBool;

			if (dict.TryGetValue("world", out var worldObj))
				switch (worldObj) {
					case WorldIdentifier worldId:
						options.WorldType       = 1;
						options.WorldIdentifier = worldId;
						break;
					case ResourceIdentifier resId:
						options.WorldType     = 2;
						options.WorldResource = resId;
						break;
					case string hashId:
						options.WorldType = 3;
						options.WorldHash = hashId;
						break;
				}

			if (dict.TryGetValue("instance", out var inId) && inId is IInstanceIdentifier iId)
				options.InstanceIdentifier = iId;

			var data = dict.TryGetValue("data", out var d) && d is JObject o
				? o
				: null;

			var connections = Array.Empty<string>();
			if (data != null && data.TryGetValue("a", out var ao) && ao is JArray ja)
				try {
					var converted = ja.ToObject<string[]>();
					if (converted != null)
						connections = converted;
				} catch {
					Logger.LogError("Failed to convert 'a' to string array");
				}

			options.Connections = connections;


			return options;
		}

		public int WorldType = 0;

		public WorldIdentifier    WorldIdentifier = WorldIdentifier.Invalid;
		public ResourceIdentifier WorldResource   = ResourceIdentifier.Invalid;
		public string             WorldHash       = null;

		public string              Title              = "Offline Session";
		public IInstanceIdentifier InstanceIdentifier = null;
		public Texture2D           Thumbnail          = null;
		public bool                DisposeOnChange    = false;
		public string              ShortName          = null;
		public bool                ChangeCurrent      = true;
		public string[]            Connections        = Array.Empty<string>();
	}
}