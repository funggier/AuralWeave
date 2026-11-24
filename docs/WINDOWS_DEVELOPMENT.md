# Windows development and build

## Verified toolchain

- Windows 10 / Windows SDK 10.0.19041.0
- Visual Studio Community 2026; MSVC toolsets detected under `C:\Program Files\Microsoft Visual Studio\18\Community\VC\Tools\MSVC`
- .NET SDK 8, 9 and 10 installed (app currently targets .NET 8 Windows)
- Build scripts locate MSVC using `vswhere` and initialize its environment with `vcvars64.bat`; the compiler does **not** need to appear in the global PATH.

## Full local build and tests

Run at the repository root in Windows PowerShell:

```powershell
.\scripts\build-windows.ps1 -RunDeviceDiagnostics
```

This compiles Native C++20 engine and its tests with `/W4 /WX`, builds all .NET projects, copies `AuralWeave.Native.dll` next to Desktop and Diagnostics outputs, runs the non-audible managed tests, and enumerates hardware endpoints. No audio stream starts during this build.

To build only native C++ plus tests:

```powershell
.\scripts\build-native.ps1 -ListDevices
```

To build just .NET when MSVC is not available:

```powershell
.\scripts\build-windows.ps1 -SkipNative
```

## Run the desktop UI

```powershell
dotnet run --project .\src\AuralWeave.Desktop\AuralWeave.Desktop.csproj -c Release
```

The UI starts with the managed backend selected. The **Native C++ WASAPI** option activates only when the DLL is found. Native uses the endpoint mix formats and supports mono/stereo Float32/PCM16. Unsupported endpoints return an error, rather than forcing a different audio device.

## Manual real-hardware muted diagnostics

These tests do open the actual capture/render devices, but send **mute** flags to the output. They require active audio endpoints.

```powershell
dotnet run --no-build -c Release --project .\src\AuralWeave.Diagnostics\AuralWeave.Diagnostics.csproj -- --smoke-native
dotnet run --no-build -c Release --project .\src\AuralWeave.Diagnostics\AuralWeave.Diagnostics.csproj -- --smoke-native-multi
```

The second selects one 192 kHz and one 48 kHz output if present. Neither is part of the automatic build.

## Optional portable publish

```powershell
.\scripts\build-windows.ps1 -PublishPortable
```

Produces a self-contained Windows x64 directory under `dist/AuralWeave-win-x64`, including the separate native DLL, without installing any audio driver. Portable publish layout is an implementation step, not a tested public release.

## Safety and rights

Do not install or replace audio drivers during routine builds. MIT license applies to original project source. Review NAudio and any RNNoise/third-party package's own license and required notices before public distribution.
