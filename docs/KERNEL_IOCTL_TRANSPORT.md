# Kernel / User Mode PCM Transport — Windows 10 Checkpoint

Date: 2026-11-25

## Current status

**Implemented in source, compiled and tested with a mock. Not installed,
signed, or validated in a running Windows kernel.** No unsigned SYS
or developer driver setup was distributed.

The WaveRT prototype has two root PnP hardware IDs in total: one
Virtual Input and one Virtual Output. Portable application copies
share these logical devices, while audio mixing/distribution is
implemented separately using user-mode PCM sessions.

## Security architecture

The unsigned kernel prototype now creates a separate WDM control
device after PortCls initializes its audio PnP handlers:

- NT name: \Device\AuralWeaveVirtualAudioControl
- Win32 path: \\.\AuralWeaveVirtualAudioControl
- Allowed principals: Local SYSTEM and Administrators only.
- Windows device ACL: D:P(A;;GA;;;SY)(A;;GA;;;BA)
- FILE_DEVICE_SECURE_OPEN protects namespace traversal.
- One private, bounded METHOD_BUFFERED IOCTL: 0x0022E000.
- A private class GUID is used for the control device, distinct from
  the MEDIA device class GUID.

The control code intercepts only IRPs addressed to the exact control
device. Every other audio-device IRP is passed through PcDispatchIrp
to PortCls. Unsupported IOCTLs are rejected. The control device is
destroyed on driver unload.

Microsoft references:
https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/portcls/nf-portcls-pcdispatchirp
https://learn.microsoft.com/en-us/windows-hardware/drivers/kernel/controlling-device-access
https://learn.microsoft.com/en-us/windows-hardware/drivers/kernel/buffer-descriptions-for-i-o-control-codes

## PCM protocol v2

One exact 80-byte, versioned header with generation and WaveRT frame position; up to 16,384 PCM payload bytes;
1–2,048 frames per audio command. Requests have a RequestId that must
be matched by the client to the reply. The kernel checks the entire
header and payload byte counts before accessing the PCM data.
There are no user-mode pointers or shared kernel mappings in the wire.

Operations:

- HELLO: report a format snapshot, no PCM.
- INPUT_WRITE: push bounded mic PCM32 into kernel input queue, return
  the number of accepted frames.
- OUTPUT_READ: pull bounded speaker PCM16 from kernel render queue,
  return the actual frames and payload.
- GET_STATUS: return actual stream format and queued frame count.

A role becomes active when its WaveRT pin enters RUN. The final pin leaving RUN clears the ring and advances that role's generation. The next RUN starts a fresh generation. Content protection for the output also clears its ring and advances its generation. The two roles maintain independent counters. WaveRT frame position counts the frames processed by that role since the generation began, including microphone underrun silence.

A transfer includes the generation from the last status read. The kernel checks the generation, active format and copy protection under the queue lock. A stale generation, idle role or protected output yields a zero-frame result. Incompatible active format returns an error. The managed client rejects an audio reply that cites a different generation. A whole device reopen still needs a new handle and a fresh service connection.

The managed worker remains timer based, accepts idle roles at startup and counts active cycles when either direction delivers frames. Its reference pump discards microphone data when the role is idle. The generation handshake guards in-flight IOCTLs, and future service-managed client channels must also purge their own buffered PCM across a reconnect. Event-driven pacing, cancellation and real kernel execution remain pending.

Two standalone checks cover packet rejection, the actual queue implementation compiled with test-only lock primitives, and managed restart races with a mock driver.

The currently advertised WaveRT formats are 48,000 Hz stereo signed
PCM32 integer for the virtual microphone and 48,000 Hz stereo signed
PCM16 integer for the virtual speaker. These are NOT IEEE Float32.
The managed client handles Float32 conversion explicitly.

The C# transport is WindowsVirtualKernelPcm.cs. The developer-only
VirtualKernelAudioPump provides one explicit nonblocking PCM step
from the mixed user-mode input bus to the kernel and from the kernel
back to the user-mode output bus. It is not a background service and
does not install or start the kernel driver.

## Multi-client model

Only a future trusted, elevated broker should own the administrator-
restricted kernel device handle. Multiple Portable Studio copies
contribute to user-mode mixed PCM buses with independent listener
channels. The broker must fan out speaker audio and mix microphone
contributors while managing crash recovery, drift, real-time pacing,
permissions and instance handoff. Ordinary programs should use the
Windows Audio Engine endpoint, not the privileged control interface.

## QA performed on Windows 10

- WDK kernel prototype compiled with secured control device and
  PortCls IRP forwarding; INF/Inf2Cat signability checks passed.
- Shared PCM packet serialization, status, sample conversion and
  oversized-input rejection were tested with a deterministic mock.
- End-to-end user-mode path from Native C++ Shared PCM mapping through
  the managed kernel-protocol mock into an independent Shared PCM
  output mapping passed with stereo channel integrity.
- Driver install status remains unchanged and no kernel module ran.

Commands:

    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-virtual-driver-prototype.ps1
    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-windows.ps1 -RunDeviceDiagnostics

Still not verified: the actual kernel control device opening, real
WaveRT PCM transfer, installed-device functionality, Kernel-mode
security at runtime, sleep/resume, clock stability, multi-user
broker isolation, Microsoft signing, and Windows 11 runtime behavior.

Keep INSTALL disabled until these items are independently verified
and a properly signed production package exists.
