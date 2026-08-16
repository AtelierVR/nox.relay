# StirlingLabs.MsQuic — Native Library Download Links

Native binaries for `StirlingLabs.MsQuic` v23.7.1 (`StirlingLabs.MsQuic.Bindings` v2.2.2).

`.nupkg` files are ZIP archives. Extract the native library from `runtimes/<rid>/native/`.

| Platform | Download URL | Native file | Arch |
|---|---|---|---|
| Linux x64 | https://api.nuget.org/v3-flatcontainer/stirlinglabs.msquic.bindings.runtime.linux-x64.openssl/2.2.2/stirlinglabs.msquic.bindings.runtime.linux-x64.openssl.2.2.2.nupkg | `runtimes/linux-x64/native/libmsquic-openssl.so` | x86_64 |
| Windows x64 | https://api.nuget.org/v3-flatcontainer/stirlinglabs.msquic.bindings.runtime.win-x64.openssl/2.2.2/stirlinglabs.msquic.bindings.runtime.win-x64.openssl.2.2.2.nupkg | `runtimes/win-x64/native/msquic-openssl.dll` | x86_64 |
| macOS | https://api.nuget.org/v3-flatcontainer/stirlinglabs.msquic.bindings.runtime.osx.openssl/2.2.2/stirlinglabs.msquic.bindings.runtime.osx.openssl.2.2.2.nupkg | `runtimes/osx/native/libmsquic-openssl.dylib` | universal (x86_64 + arm64) |

## Rebuild from source (msquic 2.2.2, OpenSSL 3 — portable)

The NuGet Linux `.so` links `libcrypto.so.1.1` (OpenSSL 1.1), which is missing on modern
systems (OpenSSL 3.x), causing `DllNotFoundException`. Rebuild with the `openssl3` TLS
backend to statically embed OpenSSL 3 — the resulting library has no external
`libcrypto` dependency.

```bash
git clone --depth 1 --branch v2.2.2 --recurse-submodules https://github.com/microsoft/msquic.git
cmake -S msquic -B build -DCMAKE_BUILD_TYPE=Release \
      -DQUIC_TLS=openssl3 -DQUIC_BUILD_SHARED=ON \
      -DQUIC_BUILD_TOOLS=OFF -DQUIC_BUILD_TEST=OFF -DQUIC_BUILD_PERF=OFF
cmake --build build --target msquic -j
# Output: build/bin/Release/libmsquic.so.2.2.2  (Linux)
```

Both `QUIC_TLS=openssl` (OpenSSL 1.1) and `QUIC_TLS=openssl3` (OpenSSL 3) are supported;
`openssl3` statically embeds OpenSSL 3 by default (`no-shared`, `no-dso`).

### Platform build matrix

| Target | Status | How |
|---|---|---|
| Linux x64 | ✅ built | `QUIC_TLS=openssl3` — already built & installed |
| macOS (x64 + arm64) | ⚠️ needs macOS | Native build on macOS (`QUIC_TLS=openssl3`), or osxcross |
| Windows x64/x86 | ⚠️ needs MSVC | msquic CMake targets MSVC; cross-build via MinGW fails (CMake MSVC-only) |
| Android | ⚠️ needs NDK | msquic supports `CMAKE_SYSTEM_NAME=Android` with NDK toolchain |
| Linux arm64 | ⚠️ needs cross cc | Requiert un compilateur `aarch64-linux-gnu-gcc` |

> Only Linux x64 can be rebuilt directly from a Linux host. macOS, Windows and Android
> must be built natively (or with their respective toolchains) because msquic's CMake is
> MSVC-oriented on Windows and there is no macOS cross-toolchain installed.

## Managed packages

| Package | Version | URL |
|---|---|---|
| StirlingLabs.MsQuic | 23.7.1 | https://api.nuget.org/v3-flatcontainer/stirlinglabs.msquic/23.7.1/stirlinglabs.msquic.23.7.1.nupkg |
| StirlingLabs.MsQuic.Bindings | 2.2.2 | https://api.nuget.org/v3-flatcontainer/stirlinglabs.msquic.bindings/2.2.2/stirlinglabs.msquic.bindings.2.2.2.nupkg |

## Transitive dependencies

| Package | Version | URL |
|---|---|---|
| StirlingLabs.Utilities.NativeLibrary | 22.9.1 | https://api.nuget.org/v3-flatcontainer/stirlinglabs.utilities.nativelibrary/22.9.1/stirlinglabs.utilities.nativelibrary.22.9.1.nupkg |
| StirlingLabs.sockaddr.Net | 22.10.0 | https://api.nuget.org/v3-flatcontainer/stirlinglabs.sockaddr.net/22.10.0/stirlinglabs.sockaddr.net.22.10.0.nupkg |
| StirlingLabs.BigSpans | 22.9.4 | https://api.nuget.org/v3-flatcontainer/stirlinglabs.bigspans/22.9.4/stirlinglabs.bigspans.22.9.4.nupkg |
| StirlingLabs.Utilities | 22.9.1 | https://api.nuget.org/v3-flatcontainer/stirlinglabs.utilities/22.9.1/stirlinglabs.utilities.22.9.1.nupkg |
| StirlingLabs.Utilities.Magic | 22.9.1 | https://api.nuget.org/v3-flatcontainer/stirlinglabs.utilities.magic/22.9.1/stirlinglabs.utilities.magic.22.9.1.nupkg |
