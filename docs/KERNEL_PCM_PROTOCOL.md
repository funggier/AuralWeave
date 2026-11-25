# Kernel / Native PCM Protocol v1 (Compile-Only Checkpoint)

This checkpoint supplies the data contract and bounded audio FIFO needed
before safely implementing a Windows kernel-to-user audio device. It does **not** install an audio device or expose any IOCTL handler.
The internal WaveRT DMA path has now been connected to per-role kernel
queues, but no PCM can yet cross securely to a Windows process.

Source under src/AuralWeave.VirtualDriver/Transport/:

- aw_pcm_wire.h — kernel-safe layout, operations, limits, status codes.
- aw_pcm_wire.cpp — fixed-sized packet validator and caller-synchronized
  bounded PCM byte-ring implementation.
- aw_pcm_wire_smoke.cpp — user-mode tests compiled on Windows 10 x64.

The same aw_pcm_wire.cpp is compiled into
src/AuralWeave.VirtualDriver/Kernel/Source/Main/Main.vcxproj by the WDK.
The original AuralWeave protocol/ring files use the project's MIT
license; the existing Microsoft-derived Kernel/ files remain MS-PL.

## Protocol limits

| Property | Contract |
| --- | --- |
| Version | 1, exact-match required |
| Header | 64 bytes, fixed-width POD |
| Roles | Input = 1, Output = 2 |
| Device slot | Exactly 1 per role |
| Supported formats | PCM16 or interleaved Float32 |
| Channels | Mono or stereo |
| Rates | 8,000–384,000 Hz |
| Frames per message | 0–2,048, operation-dependent |
| Payload | Max 16,384 bytes |
| Encapsulation | Future METHOD_BUFFERED IOCTL (not yet hooked up) |

The protocol has no raw pointers, handles, Windows paths, process IDs
or writable offsets in its wire header. Direction is checked for every
operation. Reserved fields and unknown flags are rejected, as are
length overflows, impossible formats and unsupported opcodes.

A future kernel handler must only validate **after** the I/O manager
has provided an accessible bounded system buffer and must check the
requestor identity and privileges, including impersonation and
session lifetimes, independently of this parser.

## Ring behavior

The PCM ring implementation is allocation-free and does not wait or
call Windows APIs. Its byte buffer must be allocated by the caller
(in kernel mode it must be nonpaged). Each operation uses complete
PCM frames. Full buffers reject new frames and count drops; empty
reads can fill silence and count underruns. Reset clears cursors
and counters. **Caller synchronization is mandatory** if writer and
reader execute concurrently, for example appropriate kernel locks
and verified IRQL discipline; the test implementation is not a
drop-in thread-safe kernel transport.

## Tests

Run on Windows 10:

    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-kernel-transport.ps1
    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-virtual-driver-prototype.ps1

The first command runs strict-warning MSVC tests for malformed
messages, directions, frame sizes, overflow/underflow, exact PCM
round-trip, and 64,000 frames through 1,000 ring wrap cycles.

The second builds AuralWeaveVirtualAudio.sys with the **same protocol
source**, validates the INF in Windows/Universal modes and checks
signability for representative Windows 10 and Windows 11 x64 targets.
Compilation is **not** an active driver or Microsoft signing.

Windows 10 is the development/runtime test baseline. Windows 11
support is theoretical based on compatible APIs, WDK and INF checks;
it has not been runtime-tested.

## Remaining kernel implementation

1. Design the actual authorized device interface and dispatch routine.
2. Ensure each role has a bounded kernel-side nonpaged queue and
   WAVEFORMAT matching the current negotiated stream format.
3. Connect the recording/playback WaveRT stream positions to those queues
   with correct direction, locking, cancellation and IRQL behavior.
4. Implement secure per-role owner arbitration and reconnection on
   process exit, PnP stop and device removal.
5. Perform Windows 10 actual driver runtime tests and complete signing
   before enabling Install. Windows 11 remains a theoretical target
   until independently tested.

The current Portable package must not include the unsigned SYS.


## Internal WaveRT queue integration (2026-11-25)

See WDK_WAVERT_PCM_CHECKPOINT.md. The latest kernel source now links
the protocol ring into real WaveRT DMA paths at source level,
with two independent roles, silence fallback and DRM checks.
It has been compiled by WDK but is not installed or kernel tested.
User-mode IOCTL transport, credential checking and broker are pending.


## Secured Kernel IOCTL PCM checkpoint (2026-11-25)

The unsigned Windows 10 WDK prototype now compiles a separate
SYSTEM/Administrator-only WDM control device with strict
METHOD_BUFFERED request validation. Audio PnP requests still reach
PortCls through PcDispatchIrp. Virtual Input accepts signed PCM32
and Virtual Output returns signed PCM16. The versioned protocol is
bounded to 2,048 frames per transfer and at most 16,384 bytes of PCM.

The managed WindowsVirtualKernelPcm client and the explicit
VirtualKernelAudioPump are compiled and mock-tested. A Native C++
Shared PCM input flowed through the simulated kernel protocol and
was received on an independent Shared PCM output with correct
stereo sample values. Real kernel installation/IOCTL transfer has
NOT been tested. An elevated trusted broker is not yet shipped.

See docs/KERNEL_IOCTL_TRANSPORT.md for the exact access, data layout,
QA and remaining signed-driver prerequisites. The current driver is
UNSIGNED and must NOT be installed on the host Windows 10 computer.
