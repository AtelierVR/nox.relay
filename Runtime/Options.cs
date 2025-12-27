using System.Collections.Generic;
using Nox.CCK.Utils;
using Nox.CCK.Worlds;
using UnityEngine;

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
				}

			return options;
		}

		public int WorldType = 0;

		public WorldIdentifier    WorldIdentifier = WorldIdentifier.Invalid;
		public ResourceIdentifier WorldResource   = ResourceIdentifier.Invalid;

		public string    Title           = "Offline Session";
		public Texture2D Thumbnail       = null;
		public bool      DisposeOnChange = false;
		public string    ShortName       = null;
		public bool      ChangeCurrent   = true;
	}
}