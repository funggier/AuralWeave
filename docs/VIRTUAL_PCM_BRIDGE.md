# AuralWeave Virtual PCM Bridge (Native user-mode stage)

Status (2026-11-25): the user-mode bridge works and passes Windows 10 cross-process tests. It is not a Windows Kernel audio driver and does not create virtual microphones or speakers in Windows.

## Data flow

Studio: WASAPI Capture -> ordered Filters -> physical outputs (unchanged)
                                 -> optional Virtual Input PCM Tap -> resampler -> shared-memory buffer -> user-mode consumer -> [future signed WaveRT microphone driver]

The Virtual Output (speaker-to-Studio) direction is independently supported as a shared-memory channel, but has not been connected to a WaveRT device or Studio's source selector.

## Implementation

Native modules: src/AuralWeave.Native/include/auralweave/virtual_audio_bridge.hpp, virtual_audio_bridge_api.h; src/AuralWeave.Native/src/virtual_audio_bridge.cpp, virtual_audio_bridge_api.cpp; optional tap in audio_engine.cpp.

Managed wrappers: NativeVirtualAudioBridge.cs and NativeWasapiAudioRoute.BindVirtualInputBridge in src/AuralWeave.Audio.Windows. The real audio route continues feeding its physical outputs.

A bridge is a random-name, session-local Windows file mapping with versioned, cache-line-aligned metadata, interleaved Float32 PCM, channels 1 or 2, sample rates 8-384 kHz, and a bounded capacity of 64-32768 frames. Default is 48 kHz, stereo, 4096 frames. Roles are Virtual Input (recording, 1) and Virtual Output (render, 2), only slot 1 per role. The mapping has exactly ONE producer and ONE consumer; other topologies need individual mappings. Windows Interlocked operations publish cursors and counters without locking or allocation in the audio hot path. When producer throughput exceeds consumer throughput, new samples that cannot fit are dropped and counted, never blocking real-time audio. Reader underruns can be filled with silence.

Native PCM mappings now use a v2 header (with advisory creator PID) and are
used internally in the **test-only** Mock Broker. This process-origin PID check
is not the admission policy for Windows audio clients and cannot authorize
a production elevated service. All normal Windows apps must still be able to
use the future shared-mode endpoint without AuralWeave-specific IPC.

Name randomness is not complete authentication. Current mapping permissions derive from the launching process's default security descriptor; future privileged driver access requires explicit ACL checks and secure handshake. This stage does not claim low measured end-to-end latency, hot-plug recovery, clock-drift correction or device signing.

Native exports include aw_bridge_create, aw_bridge_attach, aw_bridge_write, aw_bridge_read, aw_bridge_close, aw_bridge_get_stats and aw_bridge_last_error. Running Studio routes can use aw_route_bind_virtual_input and aw_route_unbind_virtual_input. Native Studio can also accept virtual-output: followed by an exact active Shared Memory mapping name. The .NET wrapper exposes these in development diagnostics, not yet via the public Virtual Driver installation controls.

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


## Single Virtual Input and Virtual Output topology (2026-11-25)

Only two Windows PnP model IDs are now supported: one Virtual Input
(ending VINPUT_0001) and one Virtual Output (ending VOUTPUT_0001).
All other endpoint slot numbers are rejected by the user-mode bridge.
A temporary random-name IPC session is not another Windows PnP device.

The future signed installer must serialize devnode creation and check
the complete Windows PnP inventory before installation. The manager
continues to recognize IDs 0002..0016 from the former prototype as
legacy devices for explicit cleanup only. It cannot install a second
current virtual device in either role.

## Native Virtual Output as a developer-only Studio source

Native Studio now accepts a virtual-output: mapping name as a source
in addition to capture: and loopback: hardware sources. It attaches
to a valid role=Output, slot=1 SPSC ring, polls incoming PCM at bounded
intervals, applies the ordered Filters and renders through the usual
physical output graph. An optional Virtual Input post-filter tap can
be active concurrently.

On Windows 10, a test injected 57,600 constant Float32 frames at
48 kHz, applied Gain=0.5 and read 57,599 frames from the Virtual Input
bridge. The one frame difference is the existing interpolation lookahead;
source overflow was zero. All physical audio was muted in the test.

This proves a user-mode DSP route, not a real Windows virtual speaker
or microphone. Kernel-to-user PCM transport, Windows 10/11 VM testing,
HLK checks and driver signing are outstanding.

The simulated Virtual Output diagnostic also validates that closing the
source producer causes the Native Studio route to stop cleanly. It does not
change or install Windows virtual driver devices.


## Multi-Portable ownership and kernel contract checkpoint (2026-11-25)

**Historical (superseded):** an earlier NativeVirtualAudioBridge.Create
prototype held a machine-wide role lease for the entire audio session.
This was later changed; present multi-Portable user-mode tests allow
simultaneous audio owners/listeners for the same role, while brief global
mutexes protect only administrative operations. See the approved
[VIRTUAL_DRIVER_PRODUCT_CONTRACT.md](VIRTUAL_DRIVER_PRODUCT_CONTRACT.md)
and [PORTABLE_MULTI_INSTANCE_CONTROL.md](PORTABLE_MULTI_INSTANCE_CONTROL.md).

A separate, strictly sized protocol and caller-synchronized ring for
future Kernel WaveRT interaction are implemented and compiled in the
unsigned WDK prototype. The protocol does **not** expose a functioning
Windows audio endpoint or IOCTL dispatcher. See KERNEL_PCM_PROTOCOL.md.


## Multi-client audio bus (2026-11-25)

SharedVirtualAudioBus now implements developer-only user-mode mixing
and broadcasting across multiple AuralWeave Portable copies. It
aggregates independent SPSC source rings and writes the resulting
Float32 PCM to each listener's independent ring. Normal Windows
virtual devices are still limited to one Input and one Output; the
number of user-mode client sessions does not change that limit.

The prior full-session VirtualDeviceControlLease restriction has been
replaced with a brief administrative mutex and cross-process activity
handles. A slow listener loses frames only in its own ring. Cross-folder
Windows 10 tests passed; the current unsigned kernel driver is NOT
connected to this bus. See PORTABLE_MULTI_INSTANCE_CONTROL.md and
VIRTUAL_DRIVER_INSTALLATION.md.


## WaveRT internal PCM and installer-helper checkpoint (2026-11-25)

**Build-stage only. No Windows driver was installed or signed.**

The unsigned Windows 10 WDK prototype now compiles kernel-internal,
independently locked 64-KiB PCM rings for Virtual Input and Virtual
Output. The WaveRT microphone DMA path consumes queued PCM or emits
silence; WaveRT speaker DMA copies rendered frames into its bounded
PCM queue. The Microsoft Sample sine generator, file recording worker
and runtime registry toggles were unlinked. A global single-adapter
restriction was found and changed to allow **Input + Output together**
in the driver architecture. Digital rights that forbid copying disable
render PCM mirroring and flush queued frames.

No secure kernel device IOCTL or user broker is connected yet, so the
driver is NOT a usable installed Windows audio endpoint.

The separate Windows 10 DriverSetup project now builds as a self-contained
read-only preflight utility and contains future signed-only PnP SetupAPI
logic. The production certificate SHA-256 pin is intentionally blank,
so all actual installation requests remain disabled. Uninstallation
remains a Windows Device Manager operation.

See docs/WDK_WAVERT_PCM_CHECKPOINT.md for source, design and QA.