# Windows development environment

## Toolchain already detected on this machine

- Windows 10 Pro 22H2 / Windows SDK 10.0.19041.0
- Visual Studio Community 2026 at `C:\Program Files\Microsoft Visual Studio\18\Community`
- MSVC 14.51.36231 and 14.52.36725 (x64 `cl.exe`)
- .NET SDK 8, 9 and 10 installed
- CMake available on PATH

A missing `cl.exe` from a generic PowerShell PATH **does not imply C++ is uninstalled**.
To initialize x64 C++ compiler environment:

```bat
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat"
cl /Bv
```

The native build script finds Visual Studio with `vswhere.exe` and calls `vcvars64.bat` itself.

## Native smoke

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-native.ps1 -ListDevices
```

This compiles the native graph and Windows Core Audio device enumerator and runs non-audible tests. It does **not** install a driver.

## Desktop / .NET

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-windows.ps1
dotnet run --project .\src\AuralWeave.Desktop\AuralWeave.Desktop.csproj -c Release
```

The current Desktop is a prototype. Select a source and render output, then Start or Stop. Be cautious with physical playback feedback. The app does not automatically start audio.

## Dependency note

The .NET WASAPI bridge currently references `NAudio 2.2.1` through NuGet. This dependency's packaging and license notices must be audited before a public release. The native C++ graph and endpoint enumeration use Windows SDK APIs.

## No compiler installation required

The existing MSVC toolchain passed a real C++ compile/run and native project build. Additional Visual Studio components may be installed separately as desired, but do not alter the current install while Visual Studio Installer is working.
