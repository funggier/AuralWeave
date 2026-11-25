# Multiple Portable Copies — Virtual Driver Ownership (2026-11-25)

## Design

AuralWeave can be copied, moved, or launched from multiple directories on
the same Windows 10 machine. Portable Studio settings, presets and caches are
local to each program folder. They must **not** become the authoritative
identity for a virtual audio driver installed in Windows.

Windows Plug and Play owns the installed driver nodes:

- **AuralWeave Virtual Input:** at most **one** PnP device,
  hardware ID ROOT\AURALWEAVE_VINPUT_0001
- **AuralWeave Virtual Output:** at most **one** PnP device,
  hardware ID ROOT\AURALWEAVE_VOUTPUT_0001

The manager lists actual matching Windows MEDIA devices by hardware ID,
driver service and class, not by the executable folder. Old experimental
device IDs 0002–0016 remain discoverable for explicit cleanup only.
The Windows driver prototype is not currently signed or installed;
all Install actions stay disabled.

## Independent Studios; exclusive virtual control

Multiple Studio windows may use their own *physical* capture devices,
outputs and filter chains; actual Windows hardware sharing still depends
on the audio device and driver. No global app singleton prevents
independent, ordinary Studio sessions.

Virtual audio roles require exactly **one active controller per role**.
The first AuralWeave instance that creates a user-mode Virtual Input session
takes the Input controller lease; an attempt from a different Portable
copy is rejected while that session exists. Output has a separate lease
so another copy can control the other direction at the same time.

The managed control plane implements
VirtualDeviceControlLease.TryAcquire(role) with two Windows named mutexes:

- Global\AuralWeave.VirtualAudio.Input.Controller.v1
- Global\AuralWeave.VirtualAudio.Output.Controller.v1

These names do not contain a drive letter, path, session-specific filename
or preset. Windows mutex handles are automatically abandoned when a
process exits unexpectedly. A dedicated worker thread owns each mutex,
ensuring that Dispose() remains safe even when asynchronous WPF or
.NET code calls it from a different thread.

NativeVirtualAudioBridge.Create() now acquires the corresponding
role lease **before** creating a new user-mode PCM session, and releases
it when that session is disposed or a creation error occurs. Receiver
Attach() does not acquire the writer/controller lease: existing
cross-process transport is one producer / one consumer per mapping.
The future driver broker must enforce reader ownership separately.

AuralWeaveVirtualDrivers.RequestDeviceRemovalAsync() also attempts
to obtain the appropriate role lease, then rechecks the exact Windows
PnP identity before invoking administrator-approved PnPUtil removal.
It refuses to remove a device while another AuralWeave session
controls its role. It only removes the selected devnode, not an
entire shared driver package.

These locks coordinate **cooperating AuralWeave processes**. They do
not prevent arbitrary external programs, malicious object pre-creation,
or kernel access; a signed driver / privileged management service must
verify role ownership and security descriptors independently. Other
Windows user accounts may have different access rights to the global
mutex; access failure must be treated as denial, not permission.
Future installation must serialize installation and check the PnP
inventory atomically rather than relying solely on MayInstall().

## Tested on Windows 10

Run:

    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-multiple-portable-instances.ps1

The script builds managed Diagnostics, copies the second Diagnostics
executable's dependencies into the project's ignored
build\multiple-portable-folders\Portable-B folder, then starts
*separate Windows processes from different directories*.

Assertions:

1. Input held in copy A -> Input request from copy B is denied.
2. Input held in A -> Output in B can be acquired independently.
3. Output held in A -> Output request in B is denied.
4. Release Output in A -> B can acquire Output.
5. Release Input after an asynchronous continuation -> B can acquire Input.

All five checks passed on Windows 10. No real driver is installed or
removed during these tests. This also does **not** establish that the
currently unsigned kernel prototype routes PCM to a Windows device.

## Windows 11

Windows 11 x64 is kept as a **theoretical target** using the Windows
10.0 shared driver API baseline, INF model declarations, WDK build and
signability checks. Windows 11 hardware/VM testing has **not** been
performed. Windows 10 remains the actual hardware development and QA
baseline. Do not represent Windows 11 as runtime-tested.

## Remaining integration

- Secure kernel-to-user PCM transport for the single microphone and
  speaker miniport roles, with authoritative control ownership.
- Reliable device hot-plug, owner handoff, latency and crash recovery.
- Proper signed driver packaging and administrator installation.
- UI state for ownership / conflicts once the installable driver exists.
