#nullable enable
//
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//

using System;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;
using UnityEngine;

namespace StirlingLabs.MsQuic.Bindings {
	[PublicAPI]
	[SuppressMessage("Security", "CA5392", Justification = "Manual initialization")]
	[SuppressMessage("Design", "CA1060", Justification = "They're in generated code")]
	public partial class MsQuic {
		public const string MsQuicLib = "msquic-openssl";
		public const string SaLib = "sa";

		/// <summary>Delegate for loading a native library by name (without extension).</summary>
		public delegate void NativeLibLoader(string name);

		/// <summary>
		/// Delegate that resolves the address of a native export from an already-loaded library.
		/// <para>Injected by the host (typically Nox.Relay Runtime) and backed by ILibAPI.GetSymbol —
		/// this keeps all DllImport usage confined to nox.loader.</para>
		/// </summary>
		public static Func<string, string, IntPtr> SymbolResolver;

		[SuppressMessage("Design", "CA1065", Justification = "Security critical failure")]
		static MsQuic() { }

		/// <summary>
		/// Pre-loads sa and msquic-openssl native libraries using the provided loader, and
		/// wires <paramref name="symbolResolver"/> for DllImport-free symbol resolution.
		/// Must be called before any MsQuic API is used.
		/// </summary>
		/// <param name="loader">A delegate that loads a native library by its base name (e.g. "sa", "msquic-openssl").
		/// Typically <c>Main.CoreAPI.LibAPI.Load</c>.</param>
		/// <param name="symbolResolver">Resolves a native export address from (libraryName, symbol).
		/// Typically <c>Main.CoreAPI.LibAPI.GetSymbol</c>.</param>
		public static void Init(NativeLibLoader loader, Func<string, string, IntPtr> symbolResolver = null) {
			loader(SaLib);
			loader(MsQuicLib);
			SymbolResolver = symbolResolver;
			Debug.Log($"[MsQuic] Native libraries '{SaLib}' and '{MsQuicLib}' loaded via LibAPI.");
		}

		/// <summary>Resolves a native export address using the injected resolver (DllImport-free).</summary>
		public static IntPtr ResolveSymbol(string library, string symbol) {
			var resolver = SymbolResolver;
			if (resolver == null)
				throw new InvalidOperationException(
					"MsQuic symbol resolver is not initialized. Call MsQuic.Init(loader, resolver) first.");
			return resolver(library, symbol);
		}

		/// <summary>Parameterless overload for backwards compatibility with QuicRegistration..cctor.
		/// The actual loading is done by <see cref="Init(NativeLibLoader, Func{string, string, IntPtr})"/> called from Nox.Relay.Runtime.Main.</summary>
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