# Portable PCM Owner Lifetime and Crash Recovery

Date: 2026-11-25

## Purpose

Multiple copies of AuralWeave Studio can share one *logical* Virtual Input
and one *logical* Virtual Output without creating extra PnP devices.
They participate through distinct native single-producer/single-consumer
Shared Memory mappings, mixed or broadcast by a user-mode coordinator.

The PCM data mapping stays alive as long as *any* attached process holds
it, even if the creator crashes. Consequently, the original `closed`
flag alone cannot identify a creator terminated without running its
destructor. Keeping stale mappings forever fills the 16-client limit.

## Implementation

The Native C++ owner now creates a **separate, owner-only Windows named
event HANDLE** for each random PCM mapping name:

```text
Local\\AuralWeave.PCM.v1.IN.1.{GUID}
Local\\AuralWeave.PCM.v1.IN.1.{GUID}.OwnerAlive
```

The same pattern applies to OUT. The owner never shares that event HANDLE
with a subscriber. It closes automatically if Windows terminates the
owner process unexpectedly; the original PCM mapping may remain mapped by
subscribers, but the event name disappears when the owner handle closes.

On Attach, Native C++ requires both a valid shared PCM header and a
currently available owner lifetime event. `GetStats.ClosedByOwner`
reports either the ordinary graceful-close flag or a missing owner event.
This does not require changing the PCM mapping layout, SPSC semantics, or
user-facing Windows driver/PnP IDs.

`SharedVirtualAudioBus.PruneDisconnected()` detaches stale contributors
and listeners, retaining healthy clients. `Pump()` invokes cleanup once
every 16 bounded blocks to avoid polling kernel objects for every audio
sample. This is best-effort recovery, not a real-time guarantee. With
256 frames at 48 kHz, 16 normal blocks correspond to roughly 85 ms;
actual elapsed time is dependent on the caller's scheduling.

**Security boundary:** Lifetime events show that *a HANDLE with the
expected name* remains open, not who opened it. They are **not**
authentication, authorization, a replacement for restricted Windows
ACLs or proof that the creator is trusted. A future elevated broker
must authenticate client identity and impose least-privilege per-user
access. No unsigned driver is installed by this mechanism.

## Windows 10 automated checks

```powershell
.\\scripts\\build-windows.ps1 -RunDeviceDiagnostics -PublishPortable
dotnet run --no-build -c Release --project src/AuralWeave.Diagnostics/AuralWeave.Diagnostics.csproj -- --test-virtual-bus-recovery
```

The recovery diagnostic spawns another `dotnet` process that owns an
independent PCM mapping. It keeps an attachment in the parent, kills
the owner process *without Dispose*, checks owner death, detaches the
stale source/listener, and verifies that a separate surviving source
still supplies PCM to a separate listener. Both roles are exercised.

The first test run on Windows 10 22H2 succeeded, including Native C++
build, managed build, multi-folder tests, PCM owner-kill tests and
Portable ZIP creation. After adding coverage for automatic cleanup on the 16th Pump call,
the Windows 10 diagnostics were rerun and passed with exit code 0.

## Unfinished production work

- No working signed Virtual Input/Virtual Output Windows driver.
- No installed privileged Broker, authorization protocol or IPC control.
- No hardware-timed event-driven broker loop or cancelable asynchronous IOCTL.
- No measured long-duration drift, latency or multi-user/session isolation.
- No Windows 11 runtime verification.

Virtual Driver INSTALL remains disabled until the independently signed
and tested driver package and privileged host exist.
