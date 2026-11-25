# AuralWeave Mock Broker v0.2.0 — Protocol Identity and Crash/Restart

*Historical v0.2.0 checkpoint. For the current v0.3.0 mock protocol v3 and
PCM owner-process admission, see [Broker process-origin checkpoint](BROKER_PROCESS_ORIGIN_CHECKPOINT.md). Older v0.2.0 binaries must not be mixed with v0.3.0.*

2026-11-25 | Windows 10 developer-only user-mode transport checkpoint.

## Scope

The independently launched `AuralWeave.Broker --mock` stays
**non-elevated**. It neither installs Windows virtual audio devices nor
opens the kernel `DeviceIoControl` path. It deliberately requires explicit
startup and an explicit reconnect decision. No background retry storm,
automatic kernel access or driver signing change is introduced.

## Versioned control-plane connection

The mock broker's Named Pipe is now
`AuralWeave.MockBroker.Control.v2`. Changing the pipe name avoids a
breaking protocol change being mistaken for v1 compatibility. The first
request on **every** new pipe connection must be `HELLO 2` and the server
responds:

```text
OK HELLO 2 instance=<32-hex-GUID>
```

Other commands before HELLO, including `PING` and `STATUS`, produce
`ERR PROTOCOL_VERSION` and close that connection. The one-off instance
GUID is generated when the broker process is constructed; it changes across
restart. It is a **lifetime marker**, NOT authentication, authorization, a
secret or proof of process identity. The same-user pipe boundary still
needs proper service identity, SID access rules and a privileged protocol
review before becoming a system service.

`STATUS` contains the current user-mode mapping counts, PCM pump ticks,
routing frames, received PING requests, listener dropped frames and the
instance GUID. The managed `LocalBrokerRuntimeStatus.Parse` validates
these fields strictly, and `LocalBrokerControlClient.GetStatusAsync`
verifies that the instance in STATUS matches the original HELLO on its pipe.
The heartbeat remains 4 seconds; a test checks PING counts after the client
has been idle longer than one heartbeat interval.

## Explicit, fail-closed reconnect

`LocalBrokerEndpointSession.OpenAsync(role)` owns both a **new** native
PCM SPSC mapping and its control registration. Disposing the session
stops the control connection first, then frees the mapping. This prevents
the caller from incorrectly releasing a mapping while it is registered.

When a mock broker is unexpectedly killed:

1. The old control pipe becomes broken. The managed client surfaces the
   transport failure; it does not silently pretend the old session exists.
2. The hosting program must stop using and dispose its old session.
3. After starting another broker, the caller explicitly invokes
   `LocalBrokerEndpointSession.OpenAfterRestartAsync(role, previousInstanceId)`.
4. The helper creates an **empty** PCM ring with a new random mapping name,
   performs HELLO and REGISTER, and rejects an unchanged broker instance.
5. Only *new* PCM frames are sent through that new ring. Old frames written
   to the orphaned ring for the dead broker are not replayed.

There is **no automatic reconnect policy**, background process launcher,
retry without a deadline, or playback of cached PCM. A future real endpoint
will need source-specific consent and buffer flushing for each type of audio
route.

## Test evidence and commands

```powershell
.\scripts\build-windows.ps1 -RunDeviceDiagnostics
dotnet run --no-build -c Release --project src/AuralWeave.Diagnostics/AuralWeave.Diagnostics.csproj -- --test-local-broker
dotnet run --no-build -c Release --project src/AuralWeave.Diagnostics/AuralWeave.Diagnostics.csproj -- --test-local-broker-restart
.\scripts\publish-mock-broker.ps1 -SkipNativeBuild
```

The restart test uses **two independent broker processes**. It verifies
HELLO enforcement, a first valid PCM stream, an actual hard process kill
while active clients still hold mappings, a failed old control request,
refusal to fake a restart, changed GUID and native mapping IDs, zero PCM
read before the first new sample, and correct new PCM after recovery.

The normal Broker integration test checks typed status, the automatic
heartbeat, multi-source audio mixing, malformed packet isolation, session
QUIT, unexpected pipe drop and singleton exclusion.

The `v0.2.0` Mock Broker ZIP is **separate** from the release Studio ZIP.
It is a test executable only; Windows does not enumerate it as a microphone
or speaker. It does not contain or install any unsigned WDK SYS/INF/CAT.

## Remaining blockers

- Broker ↔ Studio product UI and actual audio-clock routing not yet wired.
- Elevated service authorization and secure per-client permissions not built.
- A signed Windows Virtual Input / Output driver is not installed or tested.
- Windows 11 compatibility is theoretical until runtime QA.
- Long-duration clock drift, sleeping/hotplug, hostile same-user clients,
  ACLs and fuzzing remain to be validated before any production deployment.
