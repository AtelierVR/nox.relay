using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Nox.CCK.Utils;
using UnityEngine;
using Logger = Nox.CCK.Utils.Logger;

namespace Nox.Relay.Runtime {
	public class Options {
		public static Options From(Dictionary<string, object> dict) {
			var options = new Options();
			if (dict == null)
				return options;

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
			if (dict.TryGetValue("password", out var passwordObj) && passwordObj is string passwordStr)
				options.Password = passwordStr;
			if (dict.TryGetValue("display", out var displayObj) && displayObj is string displayStr)
				options.Display = displayStr;
			if (dict.TryGetValue("reconnect", out var reconnectObj))
				options.Reconnect = ToBool(reconnectObj, options.Reconnect);
			if (dict.TryGetValue("reconnect_attempts", out var reconnectAttemptsObj))
				options.ReconnectAttempts = ToInt(reconnectAttemptsObj, options.ReconnectAttempts);
			if (dict.TryGetValue("reconnect_delay", out var reconnectDelayObj))
				options.ReconnectDelay = ToFloat(reconnectDelayObj, options.ReconnectDelay);
			if (dict.TryGetValue("reconnect_max_delay", out var reconnectMaxDelayObj))
				options.ReconnectMaxDelay = ToFloat(reconnectMaxDelayObj, options.ReconnectMaxDelay);

			if (dict.TryGetValue("world", out var worldObj))
				switch (worldObj) {
					case Identifier worldId:
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

			if (dict.TryGetValue("instance", out var inId) && inId is Identifier iId)
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

		private static bool ToBool(object value, bool fallback) {
			try {
				return value != null && Convert.ToBoolean(value);
			} catch {
				return fallback;
			}
		}

		private static int ToInt(object value, int fallback) {
			try {
				return value != null ? Convert.ToInt32(value) : fallback;
			} catch {
				return fallback;
			}
		}

		private static float ToFloat(object value, float fallback) {
			try {
				return value != null ? Convert.ToSingle(value) : fallback;
			} catch {
				return fallback;
			}
		}

		public int WorldType = 0;

		public Identifier WorldIdentifier = Identifier.Invalid;
		public ResourceIdentifier WorldResource = ResourceIdentifier.Invalid;
		public string WorldHash = null;

		public string Title = "Relay Session";
		public Identifier InstanceIdentifier = Identifier.Invalid;
		public Texture2D Thumbnail = null;
		/// <summary>Optional instance password (sent to the relay when the instance requires one).</summary>
		public string Password = null;
		/// <summary>Optional in-room pseudonym (used instead of the account display name).</summary>
		public string Display = null;
		public bool DisposeOnChange = true;
		public string ShortName = null;
		public bool ChangeCurrent = true;
		public string[] Connections = Array.Empty<string>();

		/// <summary>Automatically reconnect after an unexpected disconnection.</summary>
		public bool Reconnect = true;
		/// <summary>Maximum number of reconnection attempts; 0 means unlimited.</summary>
		public int ReconnectAttempts = 0;
		/// <summary>Delay in seconds before the first retry; doubles after each failure.</summary>
		public float ReconnectDelay = 1f;
		/// <summary>Upper bound in seconds of the reconnect backoff delay.</summary>
		public float ReconnectMaxDelay = 30f;
	}
}