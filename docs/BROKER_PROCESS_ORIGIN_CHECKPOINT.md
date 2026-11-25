# Mock Broker v0.3.0 — PCM process-origin admission checkpoint

Date: 2026-11-25. Non-privileged Windows 10 user-mode development only.

## What changed

The stand-alone `AuralWeave.Broker --mock` now compares two origin
signals before accepting any client PCM SPSC ring:

1. The real calling process ID returned by Windows
   `GetNamedPipeClientProcessId` on the **connected server-side pipe
   handle**. It is never read from the ASCII command string.
2. The creating process ID published by the Native C++ PCM creator in a
   reserved four-byte field of its shared-memory header, before the
   native protocol's magic value is published.

These values must be equal and nonzero. If they differ, the bus disposes
its temporary mapping attachment, responds `ERR ATTACH_REJECTED`, and
leaves every existing registered stream untouched. Input contributor and
output listener registrations both enforce the comparison. Internal
broker-owned loopback mappings are attached without an external PID
requirement, since no separate control-pipe client owns them.

`SharedVirtualAudioBus.AddContributor` / `AddListener` accept an optional
expected owner PID. Existing direct user-mode hub tests that are not
fronted by a client pipe are unaffected.

**Important security limitation:** a Native PCM shared-memory header is
user-writable. A malicious same-user process with access to that mapping
can change the alleged owner PID. Even though Windows reports a true
pipe client PID, comparing it with mutable memory **is NOT
cryptographic authentication, owner attestation, user authorization or a
security boundary for an elevated service**. The purpose of this mock
check is to prevent accidental or naive cross-process PCM hijacking
while the real privilege-isolation architecture is still being designed.

The owner lifetime `.OwnerAlive` event and bounded liveness check from
the earlier prototype remain necessary to reject old or crashed PCM
mappings. They are also not a trust boundary.

## Compatibility

Native shared PCM mapping header protocol is **v2**, with names such as
`Local\\AuralWeave.PCM.v2.IN.1.{GUID}`. The new C export
`aw_bridge_owner_process_id(handle)` returns the advisory PID
for both owner and attached handles, or zero for a null handle.

The non-privileged control pipe is now
`AuralWeave.MockBroker.Control.v3`; the mandatory first request is
`HELLO 3`, and the response includes the per-broker-process instance GUID.
The old `v2` control pipe and old Native PCM `v1` mappings are
intentionally not accepted by the updated Broker. Do not mix Native DLL,
Broker, and mock-client binaries from different testing packages.

The test-only portable package version is **0.3.0**, separately named
`AuralWeave-MockBroker-v0.3.0-Windows-x64.zip`. The ordinary Studio
packaging remains Studio-only; the mock binary is never silently included.

## Windows 10 validation

Use these commands on the same repository and a Windows 10 host:

```powershell
.\scripts\build-windows.ps1 -RunDeviceDiagnostics
dotnet run --no-build -c Release --project src/AuralWeave.Diagnostics/AuralWeave.Diagnostics.csproj -- --test-local-broker-ownership
.\scripts\publish-mock-broker.ps1 -SkipNativeBuild
```

The new `--test-local-broker-ownership` starts an independent Mock
Broker and distinct .NET native PCM owner processes. It asserts:

- **Reject:** Process A's control pipe cannot register a live PCM
  mapping created by Process B, even when the mapping's exact
  non-secret random name is known.
- **Accept:** Process B creates its own Native PCM ring, connects from
  its own process, registers it, and sends real PCM to an existing
  listener owned by Process A.
- **No isolation failure:** Denied registrations do not change the
  currently attached input/output counts.
- **Crash cleanup:** Killing Process B removes its active registration
  without terminating the Broker or Process A's output listener.
- **Compatibility:** Other native C ABI FIFO, Multi Portable,
  Protocol/Heartbeat, crash/restart and read-only driver readiness
  tests continue to pass.

GitHub Actions also builds the two separate Windows-x64 packages,
audits unsigned kernel artifacts out of the ZIP, relocates the Mock
Broker portable folder and repeats the owner-process test there.

## Remaining security work before any privileged service

For a real system service with kernel handles, do **not** reuse the
same-user mock admission check as authorization. Required additional
design and test gates:

- An independently secured process-owned channel or kernel-managed
  capability which clients cannot forge by writing shared memory.
- Explicit per-user/logon-session ACLs, least-privilege roles,
  capture consent and an audited threat model for mic/audio data.
- Bounded resource quotas, attack-resistant registration protocol,
  peer/token identity validation, identity and lifetime pinning,
  fuzzing, and rejection of remote or cross-session pipe clients.
- IOCTL access only inside a broker service, plus bounded/cancelable
  kernel I/O and event-driven clocking.
- Signed driver installation, HLK/WDK checks, VM tests and separate
  physical Windows 10 and 11 device QA.

**No new Windows virtual microphone or speaker is installed here.**
Windows Driver Setup remains in read-only/no-install mode.

References:
- Microsoft GetNamedPipeClientProcessId:
  https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeclientprocessid
- Microsoft Named Pipe Security and Access Rights:
  https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights
