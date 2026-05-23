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
					? (".dll", DllImportSearchPath.AssemblyDirectory)
					: (".so", DllImportSearchPath.AssemblyDirectory);

		public static string[] Folders {
			get {
				// Determine the platform-specific arch subfolder used by Unity's build pipeline
				string arch = null;
				if (RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.X64)
					arch = "x86_64";
				else if (RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64)
					arch = "ARM64";

				var assemblyDir = new FileInfo(new Uri(typeof(MsQuic).Assembly.Location).LocalPath).Directory!.FullName;
				var pluginsBase = Path.Combine(Application.dataPath, "Plugins");

				return new[] {
					// 1. Platform arch subfolder inside Plugins/ (standard Unity build layout)
					arch != null ? Path.Combine(pluginsBase, arch) : null,
					// 2. Root Plugins/ folder fallback
					pluginsBase,
					// 3. Assembly directory (Managed/ in builds, useful in editor)
					assemblyDir,
					// 4. Editor package Plugins/ folder
					Path.Combine(Application.dataPath, "..", "Packages", "nox.relay", "Plugins"),
				}.Where(f => f != null).ToArray() as string[];
			}
		}

		[SuppressMessage("Design", "CA1065", Justification = "Security critical failure")]
		static MsQuic() { }

		/// <summary>
		/// Pre-loads sa and msquic-openssl native libraries.
		/// Must be called before any MsQuic P/Invoke is used.
		/// </summary>
		public static void Init(string[] folders, string extension) {
			LoadNativeLib(SaLib, folders, extension);
			LoadNativeLib(MsQuicLib, folders, extension);
		}

		/// <summary>Convenience overload using the built-in Folders / Extension fallback.</summary>
		public static void Init() {
			var (ext, _) = Extension;
			Init(Folders, ext);
		}

		private static void LoadNativeLib(string libName, string[] folders, string extension) {
			var filename = libName + extension;
			var path = folders
				.Select(folder => Path.Combine(folder, filename))
				.FirstOrDefault(File.Exists);

			if (path == null)
				throw new DllNotFoundException($"Could not find {filename} in any of the following locations: {string.Join(", ", folders)}");
			
			path = Path.GetFullPath(path);

			Debug.Log($"Loading {filename} from {path}");
			NativeLibrary.Load(path, typeof(MsQuic).Assembly, Extension.Item2);
		}

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