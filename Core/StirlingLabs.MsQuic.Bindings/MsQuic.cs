#nullable enable
//
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using StirlingLabs.Utilities;
using UnityEngine;

#if NET6_0_OR_GREATER
using NativeLibrary = System.Runtime.InteropServices.NativeLibrary;
#endif

namespace StirlingLabs.MsQuic.Bindings {
	[PublicAPI]
	[SuppressMessage("Security", "CA5392", Justification = "Manual initialization")]
	[SuppressMessage("Design", "CA1060", Justification = "They're in generated code")]
	public partial class MsQuic {
		public const string MsQuicLib = "msquic-openssl";
		public const string SaLib = "sa";
		
		public static (string, DllImportSearchPath) Extension
			=> RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
				? (".dylib", DllImportSearchPath.AssemblyDirectory)
				: RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
					? (".dll", DllImportSearchPath.LegacyBehavior)
					: (".so", DllImportSearchPath.AssemblyDirectory);

		public static string[] Folders
			=> new[] {
				new FileInfo(new Uri(typeof(MsQuic).Assembly.Location).LocalPath).Directory!.FullName,
				Path.Combine(Application.dataPath, "Plugins"),
				Path.Combine(Application.dataPath, "..", "Packages", "nox.relay", "Plugins")
			};

		[SuppressMessage("Design", "CA1065", Justification = "Security critical failure")]
		static MsQuic() {
			// Pre-load sa.dll (StirlingLabs.sockaddr native) so P/Invoke resolution succeeds
			LoadNativeLib(SaLib);
			// Pre-load msquic-openssl.dll
			LoadNativeLib(MsQuicLib);
		}

		private static void LoadNativeLib(string libName) {
			var filename = libName + Extension.Item1;
			var path = Folders
				.Select(folder => Path.Combine(folder, filename))
				.FirstOrDefault(File.Exists);

			if (path == null)
				throw new DllNotFoundException($"Could not find {filename} in any of the following locations: {string.Join(", ", Folders)}");
			
			path = Path.GetFullPath(path);

			Debug.Log($"Loading {filename} from {path}");
			NativeLibrary.Load(path, typeof(MsQuic).Assembly, Extension.Item2);
		}

		public static void Init() { }

		public static void AssertSuccess(int status)
			=> Assert(StatusSucceeded(status), status);

		public static void AssertNotFailure(int status)
			=> Assert(!StatusFailed(status), status);


		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsPending(int status)
			=> status == QUIC_STATUS_PENDING;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsContinue(int status)
			=> status == QUIC_STATUS_CONTINUE;


		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsSuccess(int status)
			=> StatusSucceeded(status);


		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool IsFailure(int status)
			=> StatusFailed(status);

		[AssertionMethod]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static void Assert([AssertionCondition(AssertionConditionType.IS_TRUE)] bool condition, int status) {
			if (!condition)
				throw new MsQuicException(status);
		}
	}
}