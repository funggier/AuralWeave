# Multi-client Virtual Kernel PCM Coordinator — Source Checkpoint

Date: 2026-11-25

## Scope and evidence

**Implemented in C# source and wired into diagnostics and Windows CI.
A successful Windows build, new hardware test and signed driver are NOT
yet confirmed for these commits.** These are developer-only user-mode
components, not an installed virtual microphone or speaker.

The current audio device model remains exactly **one Virtual Input +
one Virtual Output per Windows computer**. Multiple Portable copies
have their own app settings and user-mode Shared PCM mappings, not
additional kernel devices.

### PCM path

```text
Portable A -- microphone contribution --[SPSC]--+
                                                |
Portable B -- microphone contribution --[SPSC]--+--> SharedVirtualAudioBus
                                                      (mix, clamp)
                                                     |
                                             Virtual Input mapping
                                                     |
                                             VirtualKernelAudioPump
                                                     |
                                       privileged PCM32 INPUT_WRITE
                                                     |
                                         unsigned WaveRT prototype
                                              (NOT installed)

                                         PCM16 OUTPUT_READ
                                                     |
                                             VirtualKernelAudioPump
                                                     |
                                             Virtual Output mapping
                                                     |
                                             SharedVirtualAudioBus
                                               (independent fanout)
                                                     |
                                   +-----------------+------------------+
                                   |                                    |
                           [SPSC] listener A                  [SPSC] listener B
```

Each mapping is single-producer/single-consumer. The two shared buses
connect independent mappings, not multiple writers to the same ring.
The coordinator owns one intermediate mapping per role. One call to
`VirtualKernelAudioCoordinator.PumpOnce()` performs:

1. Read microphone contributors; sanitize/clamp and mix to the kernel-facing input mapping.
2. Send the mixed microphone PCM32 to the mock kernel exchange.
3. Read speaker PCM16 from the mock exchange and convert to Float32.
4. Fan the speaker samples out into independent listener mappings.

Adding/removing participants is explicit and must occur between steps,
not simultaneously with the worker loop. This version is intentionally
not a discoverable service or automatic cross-process registry.

### Worker behavior

`VirtualKernelAudioWorker` is single-use and must be explicitly
started by the future trusted host. Startup queries both roles and
requires exactly 48 kHz, stereo, signed PCM32 Input and signed PCM16
Output. Missing or incompatible streams reject startup. The worker:

- Coalesces missed timer ticks; does **not** replay an unbounded audio backlog.
- Performs at most one bounded `PumpOnce` per tick.
- Stops on DeviceIoControl/data-path exceptions and exposes `LastFailure` and `Completion`.
- Supports awaited and idempotent `StopAsync`, including no activity after stop.
- Never silently restarts, escalates privilege, installs/uninstalls a driver, or changes Windows PnP.

**This is not production timing.** Windows `PeriodicTimer` resolution
and synchronous DeviceIoControl mean 256 frames/48 kHz is a target
cadence, not a measured low-latency guarantee. A production broker
needs event-driven audio clocks, drift control, cancelable I/O,
bounded queue recovery and least-privilege per-client IPC.

### Safe mock QA

These tests use the native Float32 Shared PCM library and a deterministic
`IVirtualKernelPcmExchange` fake. They open no kernel device.

```powershell
.\scripts\build-windows.ps1 -RunDeviceDiagnostics
dotnet run --no-build -c Release --project src/AuralWeave.Diagnostics/AuralWeave.Diagnostics.csproj -- --test-kernel-pcm-client
dotnet run --no-build -c Release --project src/AuralWeave.Diagnostics/AuralWeave.Diagnostics.csproj -- --test-kernel-pcm-worker
```

The worker/coordinator tests cover:

- Explicit Start, async Stop, repeated Stop, and single-use constraint.
- Rejection of disconnected/non-negotiated WaveRT endpoints.
- Simulated IOCTL disconnection aborts processing with observable error.
- Two independent microphone mappings mix into one PCM32 kernel channel.
- Two speaker listeners each receive the same PCM16-converted signal.
- Removing a contributor does not interrupt remaining contributors.
- A scheduled coordinator processes the full mix/kernel/fanout sequence.

### Before product deployment

- Verify GitHub Actions actually completes the new checks; fix errors before declaring test-ready.
- Test signed Virtual Input/Output endpoints only on an isolated Windows 10 machine or VM.
- Never distribute the current unsigned WDK prototype; leave INSTALL disabled.
- Design and independently review a trusted process/service for privileged
  kernel access; applications themselves must remain unprivileged.
- Implement authentication and authorization of each PCM contributor,
  restricted ACLs, crash-safe cross-session discovery and cleanup.
- Measure drift, jitter, latency, safety against feedback/DRM leaks,
  and recoverability under sleep/resume and endpoint hotplug.
- Validate Windows 11 at runtime separately before claiming support.
