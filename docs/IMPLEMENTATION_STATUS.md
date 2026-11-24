# AuralWeave implementation status

**Verified on Windows 10, 24 November 2026.** This is an engineering prototype, **not a production release**.

## Implemented and verified

- Visual Studio Community 2026, MSVC and Windows SDK were detected. C++20 smoke build and full native builds completed without compiler warnings or errors.
- **Native WASAPI backend:** `AuralWeave.Native.dll` opens a real WASAPI capture or loopback endpoint and multiple shared-mode render endpoints. It uses each endpoint's actual mix format rather than assuming 48 kHz.
- **Independent format adaptation:** supports 1–2 channel Float32 or PCM16, stream-resamples each output independently using a bounded linear interpolator/ring queue. **The interpolation is not release-quality anti-aliasing.**
- **Multiple outputs with different sample rates:** a muted real-device diagnostic successfully used Line In 192 kHz → Realtek USB Headphones 192 kHz plus ASUS VG249 48 kHz. For a 2.5 s test, 483,840 capture frames, zero reported output ring overruns, and no reported native error.
- **Single output:** muted native real-device diagnostic Line In → Realtek USB Headphones passed with 291,840 frames and no reported errors.
- **Windows desktop UI:** a WPF route editor chooses Input, per-route unique Outputs, ordered Gain instances that can repeat, and separate start/stop controls. Managed NAudio backend is the safe default; optional experimental Native C++ backend is available when the native DLL is deployed.
- **Unit/smoke tests:** 13 managed Core checks (output selection, repeated Gain effects, processing, bypass, PCM gain) and 16 native Core/Resampler tests passed.
- Native worker runs in a COM-initialized audio thread. A stop event synchronizes shutdown; bounded resampler queues prevent unlimited accumulated buffers.

## Deliberately unfinished / not verified

- RNNoise still **not implemented**. Noise selection is Bypass; RNNoise UI entry is disabled.
- Only the **Gain** effect is implemented. EQ/Voice Changer/AI effects remain planned.
- Audio was tested in **muted** mode. Audible output quality, audio-feedback avoidance in physical rooms, long-duration synchronization and live user acceptance have not been validated.
- The linear interpolation resampler lacks proper low-pass anti-alias filtering; high-quality sample-rate conversion is required before full release.
- Independent audio-device clocks can drift; bounded FIFO handles excess frames but is not a clock-drift PLL or adaptive resampler.
- Output unplug/replug handling, per-sink error isolation, latency measurement, true live graph swapping, presets, virtual audio driver, virtual microphone and multi-route UI are not complete.
- Native `aw_dropped_render_frames` counts queue **overruns**; it is not a comprehensive underrun/dropout counter.
- The managed backend remains available for devices with more than two channels or formats not yet supported by native.
- Current tests do not certify audio fidelity or guarantee no popping/glitches.

## How to verify

From the repository root on Windows:

```powershell
.\scripts\build-windows.ps1 -RunDeviceDiagnostics
dotnet run --no-build -c Release --project .\src\AuralWeave.Diagnostics\AuralWeave.Diagnostics.csproj -- --smoke-native
dotnet run --no-build -c Release --project .\src\AuralWeave.Diagnostics\AuralWeave.Diagnostics.csproj -- --smoke-native-multi
dotnet run --project .\src\AuralWeave.Desktop\AuralWeave.Desktop.csproj -c Release
```

The last two diagnostics activate physical audio endpoints **muted**. They are never run automatically from a normal build script. A standalone Native GUI selection requires `AuralWeave.Native.dll` to be present next to the executable, which `build-windows.ps1` arranges.

## Immediate priorities

1. Harden native buffering, output recovery, clock synchronization and high-quality sample rate conversion.
2. Real audible QA with manual user-controlled volume, repeated start/stop, long soak and failure injection.
3. Implement RNNoise behind a separate Noise Processor interface and test frame adaptation.
4. Expand effects plugin interface beyond Gain, presets, device hotplug and portable packaging.
5. Native virtual endpoints require a separately installed and appropriately signed Windows driver.
