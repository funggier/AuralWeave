# AuralWeave local Mock Broker (25 November 2026)

## Purpose / scope

This is a **separate, non-elevated, developer-only** Windows x64 process.
It lets multiple AuralWeave copies share an independently owned user-mode
Float32 PCM bus via local Windows Named Pipes, with a single mic mix and
independent speaker listeners. The first implementation loops mixed PCM
back to listeners strictly in user mode.

**It is NOT a Windows Audio endpoint, a privileged system service, an
audio driver, a functioning Virtual Input/Virtual Output device, or a
production broker.** It never opens `\\.\\AuralWeaveVirtualAudioControl`,
touches an unsigned WDK driver, changes PnP, installs a service, or enables
Windows Test Signing.

The normal AuralWeave Studio ZIP remains Studio-only. The optional mock
broker is distributed, if at all, as a separately named testing artifact
to avoid implying that its virtual microphone/speaker is installable.

## Host and protocol

- Console host: `src/AuralWeave.Broker`.
- Core: `LocalVirtualAudioBroker` in the Windows Audio library.
- Managed client API: `LocalBrokerControlClient.ConnectAsync(ownedMapping)`.
- Native PCM sessions: `NativeVirtualAudioBridge.Create(role, 1, 48000, 2)`.
- Named Pipe: `AuralWeave.MockBroker.Control.v3`, byte-mode,
  `PipeOptions.CurrentUserOnly`, local machine only.
- A per-session owner-thread mutex prevents duplicate cooperative brokers.
- At most **eight concurrent pipe sessions**; each connection registers
  at most one PCM mapping. A mapping name is checked for an exact role,
  one device slot, GUID format and a valid, live Native bridge header.
- PCM contributors are mixed/clamped and fanned out to independent
  listener rings; no Windows kernel audio device exists.
- IPC is bounded to 200 printable ASCII characters per command or
  response. Invalid or oversized input closes only the offending pipe.

Protocol v3 (one command per newline; v1/v2 clients are not compatible):

| Client request | Typical response | Description |
|---|---|---|
| `HELLO 3` | `OK HELLO 3 instance=<GUID>` | Required first command; negotiate version and read instance lifetime ID |
| `PING` | `OK PONG` | Check control plane |
| `STATUS` | `OK STATUS input=N output=N ticks=N frames=N` | User-mode counters |
| `REGISTER INPUT <mapping>` | `OK REGISTERED INPUT` | Attach one mic contributor |
| `REGISTER OUTPUT <mapping>` | `OK REGISTERED OUTPUT` | Attach one speaker listener |
| `QUIT` | `OK BYE` | Explicitly unregister and close |

Incorrect mapping names return `ERR INVALID_MAPPING`; duplicate/incompatible
mappings return `ERR ATTACH_REJECTED`. Connections must send a command or
heartbeat at least once every 15 seconds. The managed client sends `PING`
automatically every 4 seconds, serializes requests and exposes failures.

Changes to membership and user-mode mixing are under one lock; one client's
disconnect cannot reorder/dispose another active ring. When a Native
mapping owner crashes, existing SPSC owner-lifetime checks remove the stale
channel. The listener never steals another listener's PCM.

## Windows 10 build and tests (no driver)

Run in the repository directory on Windows with .NET 8 and Visual Studio C++:

```powershell
.\scripts\build-windows.ps1 -RunDeviceDiagnostics
dotnet run --no-build -c Release --project src/AuralWeave.Diagnostics/AuralWeave.Diagnostics.csproj -- --test-local-broker
dotnet run --no-build -c Release --project src/AuralWeave.Broker/AuralWeave.Broker.csproj -- --mock
```

The last command runs an idle test broker until Ctrl+C. Opening the mock
broker alone does not route any audio and does not create Windows PnP devices.
Local Windows 10 22H2 validation: the full native/managed build, kernel
PCM mock tests and Broker integration passed; a standalone Mock Broker ZIP
was extracted into a different directory and passed the same integration.

The integration test starts another process automatically and verifies
multiple independent input clients, one output listener, PCM mixing,
clean QUIT, unexpected pipe loss, wrong mapping name, oversized wire
input, singleton protection and continuing audio after a disconnect.

For a **standalone portable test bundle** with .NET runtime included:

```powershell
.\scripts\publish-mock-broker.ps1
```

Unpack `dist/AuralWeave-MockBroker-v0.3.0-Windows-x64.zip` and run
`Start-MockBroker.cmd`. The launcher keeps .NET bundle extraction under
the portable folder. No admin privileges or installer are required.
The mock broker requires a separate PCM test client; it does not by itself
appear as a Windows microphone or speaker.

## Security and product blockers

`CurrentUserOnly` protects this *same-user* test pipe from other users;
it is **not sufficient for an elevated broker**. A process running as the
same user can still impersonate another mock client or interfere with that
user's PCM. Owner-lifetime events detect crashes, not trusted identity.

Before any privileged broker or real kernel driver is distributed:

1. Design service identity, bounded authorization scopes and explicit
   per-user/per-app SID checks; require strict pipe security descriptors.
2. Define a versioned, authenticated control protocol with peer identity
   verification and resource quotas independent of mapping names.
3. Isolate all privileged IOCTL handles from Studio clients; audit
   input/output capture permissions and DRM behavior.
4. Implement safe cancellation/timeout for potentially blocked kernel I/O,
   actual event-driven audio clock pacing and sample-rate drift correction.
5. Run broker kill/restart, suspend/resume, hotplug, cross-user denial,
   fuzzing, hardware QA, latency measurements, HLK and production signing
   on Windows 10; then separately validate Windows 11.

The ordinary Studio INSTALL option **must remain disabled** for this
prototype. No signed driver package is available.

## Protocol v3 / process ownership and restart

The Mock Broker now requires `HELLO 3` before all other commands and returns
a new instance GUID for each process lifetime. The managed
`LocalBrokerRuntimeStatus` validates typed STATUS fields and exposes
heartbeat counts and dropped speaker frames. The opt-in
`LocalBrokerEndpointSession` owns its Native FIFO and its pipe together;
`OpenAfterRestartAsync` creates a new PCM FIFO only when a different
Broker instance is available. It does not automatically reconnect or
replay old samples. See [crash/restart checkpoint](BROKER_CRASH_RESTART_CHECKPOINT.md).

The separate testing ZIP is now `AuralWeave-MockBroker-v0.2.0-Windows-x64.zip`.
Older v1/v2 client executables cannot connect to the v3 pipe. The Native PCM header is now v2 and includes an advisory creator Process ID; the broker checks it against the PID of the connected Windows Named Pipe client before accepting a registration. This check is not a security boundary against same-user tampering. Details: [PCM process-origin checkpoint](BROKER_PROCESS_ORIGIN_CHECKPOINT.md).
