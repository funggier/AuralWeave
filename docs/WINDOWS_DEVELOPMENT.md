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

The native MSVC C++ compiler is mandatory; there is no managed audio fallback or .NET-only product build.

## Run the desktop UI

```powershell
dotnet run --project .\src\AuralWeave.Desktop\AuralWeave.Desktop.csproj -c Release
```

The UI uses only the **Native C++ WASAPI** engine. The native DLL must be present. Native supports mono/stereo Float32/PCM16 endpoint mix formats. Unsupported endpoints return an error rather than falling back to another audio engine.

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

Produces `dist/AuralWeave-win-x64` and `dist/AuralWeave-v<version>-Windows-x64.zip`. The packaged `RELEASE_NOTES.md` combines `docs/releases/v<version>.md` with the [VB-CABLE guide](VB_CABLE.md).

For each GitHub Release, upload the versioned ZIP and use the generated `dist/AuralWeave-win-x64/RELEASE_NOTES.md` as the release body. With an existing tag:

```powershell
$tag = 'v0.3.1'
gh release create $tag ".\dist\AuralWeave-${tag}-Windows-x64.zip" `
    --verify-tag --notes-file .\dist\AuralWeave-win-x64\RELEASE_NOTES.md
```

## Safety and rights

Do not install or replace audio drivers during routine builds. MIT license applies to original project source. Verify notices and licenses for future RNNoise and other third-party modules before public distribution. NAudio is not included.
