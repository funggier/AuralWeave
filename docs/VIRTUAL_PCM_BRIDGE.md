# AuralWeave Virtual PCM Bridge (Native user-mode stage)

Status (2026-11-25): the user-mode bridge works and passes Windows 10 cross-process tests. It is not a Windows Kernel audio driver and does not create virtual microphones or speakers in Windows.

## Data flow

Studio: WASAPI Capture -> ordered Filters -> physical outputs (unchanged)
                                 -> optional Virtual Input PCM Tap -> resampler -> shared-memory buffer -> user-mode consumer -> [future signed WaveRT microphone driver]

The Virtual Output (speaker-to-Studio) direction is independently supported as a shared-memory channel, but has not been connected to a WaveRT device or Studio's source selector.

## Implementation

Native modules: src/AuralWeave.Native/include/auralweave/virtual_audio_bridge.hpp, virtual_audio_bridge_api.h; src/AuralWeave.Native/src/virtual_audio_bridge.cpp, virtual_audio_bridge_api.cpp; optional tap in audio_engine.cpp.

Managed wrappers: NativeVirtualAudioBridge.cs and NativeWasapiAudioRoute.BindVirtualInputBridge in src/AuralWeave.Audio.Windows. The real audio route continues feeding its physical outputs.

A bridge is a random-name, session-local Windows file mapping with versioned, cache-line-aligned metadata, interleaved Float32 PCM, channels 1 or 2, sample rates 8-384 kHz, and a bounded capacity of 64-32768 frames. Default is 48 kHz, stereo, 4096 frames. Roles are Virtual Input (recording, 1) and Virtual Output (render, 2), slots 1-16. The mapping has exactly ONE producer and ONE consumer; other topologies need individual mappings. Windows Interlocked operations publish cursors and counters without locking or allocation in the audio hot path. When producer throughput exceeds consumer throughput, new samples that cannot fit are dropped and counted, never blocking real-time audio. Reader underruns can be filled with silence.

Name randomness is not complete authentication. Current mapping permissions derive from the launching process's default security descriptor; future privileged driver access requires explicit ACL checks and secure handshake. This stage does not claim low measured end-to-end latency, hot-plug recovery, clock-drift correction or device signing.

Native exports include aw_bridge_create, aw_bridge_attach, aw_bridge_write, aw_bridge_read, aw_bridge_close, aw_bridge_get_stats and aw_bridge_last_error. Running Studio routes can use aw_route_bind_virtual_input and aw_route_unbind_virtual_input. The .NET wrapper exposes these in development diagnostics, not yet via the public Virtual Driver installation controls.

## Verification

scripts/build-native.ps1 compiles and exercises real Windows cross-process PCM transfer, mono/stereo isolation, ring wrap, overrun/underrun, C ABI, and graceful closure. A second process successfully sent 16,000 frames through the buffer.

For a separate **opt-in** live audio capture test, build the Diagnostics project, place the freshly built AuralWeave.Native.dll beside its executable, and run:

    dotnet run --no-build -c Release --project src/AuralWeave.Diagnostics/AuralWeave.Diagnostics.csproj -- --smoke-virtual-bridge

The test runs for about 1.2 seconds with one physical output muted, creates a temporary shared virtual-input PCM bridge, applies a real native Gain filter, and verifies user-mode consumption. It never installs a Windows driver or creates persistent audio data files.

## Remaining to reach real Windows 10 + Windows 11 virtual endpoints

- Implement safely authorized kernel-side WaveRT PCM transport, one isolated context per PnP instance, bounded copy and appropriate IRQL handling.
- Wire the Virtual Output receive direction into Studio's source interface and add reliable lifecycle/hot-plug/clock management.
- Validate both directions and concurrent instances in disposable Windows 10 22H2 and Windows 11 VMs; meet HLK and Microsoft signing requirements.
- Only after compatibility and signing are proven, enable optional per-device Install controls in the existing Virtual Driver page.

The Kernel prototype is separately MS-PL licensed. The new user-mode PCM Bridge is MIT. Do not ship or install the unsigned .sys.

## Repeatable command

Run scripts/test-virtual-pcm-bridge.ps1 for offline C++ transport tests.
Add -WithLiveAudio to enable the optional 1.2-second muted physical
capture diagnostic. The test never creates or installs Windows drivers.
