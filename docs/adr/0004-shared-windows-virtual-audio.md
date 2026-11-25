# ADR 0004 — Machine-wide Windows virtual audio and Studio bridge

Status: accepted architecture for implementation and signed VM validation, 2026-11-25. See the [product contract](../VIRTUAL_DRIVER_PRODUCT_CONTRACT.md).

## Endpoint and data ownership

- The signed package and kernel service provide one root MEDIA device for each reserved hardware ID. The recording role or playback role can exist alone. Both endpoints remain discoverable with Studio closed.
- Windows Audio Engine owns Shared Mode mixing of ordinary playback sessions and delivery to independent recording sessions. The WaveRT queues must serve the corresponding system stream. Verify the negotiated WaveRT pins, modes and stream count in a VM before allowing additional kernel streams to touch a role's queue. Multiple kernel consumers must never drain the same microphone FIFO competitively.
- Virtual Input (recording) receives the mix of separately authorized Studio post-filter sources. The bridge mixes one bounded block per source, clamps once after summing, and hands the kernel one microphone feed. A stopped source contributes silence.
- Virtual Output (playback) receives Windows' existing playback mix. The bridge reads that driver stream once, then delivers the same frames to separate Studio listener queues. A delayed listener can drop its own audio and cannot stall or drain other listeners. Each Studio runs its own native filters, hardware routing and portable settings.
- Ordinary Windows clients use Windows audio permissions. The bridge's internal control handle, pipe admission and listener limits apply to the bridge only. Studio opens physical and virtual Windows endpoints in Shared Mode. Test requests for Exclusive Mode and document the observed Windows policy.

[Microsoft WASAPI and Windows Audio Engine](https://learn.microsoft.com/en-us/windows/win32/coreaudio/wasapi)

## Service, permissions and isolation

Install a separately signed service through the driver package installer, with its binaries in a machine-owned directory and an SCM lifetime independent of Portable Studio. Target an isolated service identity with only the required Windows privileges and a dedicated service SID. Provision an explicit ACL for the kernel PCM control interface that permits the service SID. The reference SYSTEM/Administrator ACL is a prototype. Use an independently validated I/O access mode and keep PortCls PnP and KS dispatch on their existing path.

Studio-facing private IPC is local-only. Admit the connection with an explicit DACL and independently evaluate the Windows user and logon SID from the authenticated pipe client token. The service must verify its authorization and produce separate grants for microphone contribution and speaker-mix listening. On Windows, pipe write permissions must avoid the access bit that also allows creating a pipe instance. Authenticate the production server before clients send PCM. Start with bounded service-owned queues and bounded message I/O. If a later shared-memory optimization is required, allocate mappings on the service side and grant minimum access per direction. Treat sizes, cursors and samples supplied by a client as untrusted, even in a service-created mapping. The mock's client-supplied mapping name and writable creator PID play no role in authorization.

The virtual speaker can carry a system mix and the virtual microphone can be recorded via normal Windows permissions. Private Studio bridge access therefore defaults to denied and requires explicit directional consent in the authorized interactive logon. Revoke it on logoff/session switch, purge queued samples, and stop bridging where cross-logon endpoint isolation has not been verified. Adding support for simultaneous Windows logons requires proof that the private bridge cannot disclose another logon's audio. Multiple Portable Studios in an authorized logon can connect concurrently.

[Microsoft service SIDs](https://learn.microsoft.com/en-us/windows/win32/api/winsvc/ns-winsvc-service_sid_info) · [Microsoft named pipe access rights](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights)

## Clock and loss behavior

Use each role's WaveRT stream clock and notifications for progress, with the negotiated geometry checked per role. Timestamp or sequence PCM against that domain. Normalize Studio inputs with the native engine and absorb drift with bounded queues and rate correction. Monitor per-role underruns, overflow and discontinuity without retaining audio. The compile-only bridge currently transfers 48 kHz stereo PCM32 into the mic and PCM16 out of the speaker. The synchronous IOCTL and periodic mock are development stages.

Require bounded/cancelable IOCTL, role and service generation changes, queue reset on disconnect/driver restart, and explicit registration for a fresh session. Driver returns silence for an empty microphone queue. A role opening or closing must not suspend the other. Protected playback remains protected and is never mirrored when Windows disallows copying. Run Windows 10 signed VM QA for independent pin/role lifetimes, clock and drift, cancellation, service crash/silence, restart, and Windows Shared Mode under multiple ordinary apps before enabling any installation path. Repeat runtime checks on Windows 11 separately.

## Setup lifecycle

Setup is the sole holder of the machine-wide installation mutex. It acquires it after elevation and re-reads PnP before any change. Both roles share the setup transaction lock and preserve independent audio lifetimes. A role with an existing, partially registered, legacy or duplicate node is ineligible for another install. Package staging, trust, rollback, Device Manager removal and restart behavior require signed VM verification. If Apps & Features removal is added, register one durable installer-owned uninstaller.
