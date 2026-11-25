# WaveRT PCM Kernel Staging — Windows 10 Compile Checkpoint

Status: 2026-11-25, **WDK build and static/user-mode QA passed only**.
The driver is **NOT signed, installed, kernel-runtime-tested, or ready
for GitHub Release / production use**.

## What is implemented in the unsigned driver source

One root PnP device is reserved for Virtual Input, and one for
Virtual Output. A prior Microsoft-Sample global adapter count restricted
the driver to one adapter total. It has been replaced by a bounded
two-adapter counter, with rollback on allocation failure. The driver's
StartDevice selects exactly one endpoint direction from the root
Hardware ID. Hardware-ID uniqueness must still be verified on an
actually installed driver, not only inferred from INF tests.

The new AuralWeave-original MIT sources are:

- src/AuralWeave.VirtualDriver/Kernel/Source/Main/aw_kernel_pcm_queue.h
- src/AuralWeave.VirtualDriver/Kernel/Source/Main/aw_kernel_pcm_queue.cpp

The source is compiled by the WDK project alongside the already tested
64-byte versioned user/kernel PCM protocol and bounded
aw_pcm_wire.cpp ring implementation.

Each role owns a distinct, static, 65,536-byte resident PCM buffer,
protected by a Windows KSPIN_LOCK. No allocation, file operation,
blocking wait, or caller-controlled pointer occurs in the WaveRT
data callbacks. Each role's attached stream count, exact rate,
channel count and sample bit depth are tracked in the kernel.
The first stream initializes the role's ring; matching additional
streams can attach. Incompatible format streams are not bridged:
Virtual Input emits silence, and Virtual Output skips the ring.

Supported raw frame layouts in this staging layer are mono/stereo
with 16 or 32 bits per sample and 8–384 kHz. This layer does not
perform resampling or interpret the sample subtype; a future
authenticated broker must negotiate and verify precise WAVEFORMAT
including PCM versus IEEE Float32.

- **Virtual Input / recording:** the WaveRT capture DMA path
  reads already-queued PCM into the mapped buffer. It writes silence
  into unread/unsupported portions instead of generating the
  Microsoft Sample sine tone.
- **Virtual Output / playback:** the WaveRT render path copies
  complete frames into its own bounded ring for a future user-mode
  consumer. Overflow is recorded; the audio callback never waits.
- **Digital rights:** protected output content is not forwarded.
  DRM CopyProtect or DigitalOutputDisable causes the output PCM
  queue to flush, and copying from that stream remains disabled.
- **Shutdown:** a WaveRT stream detaches after notification timers
  and queued DPC work are stopped. The final stream of each role
  resets its ring cursors and negotiated format.
- **No audio-file capture:** the inherited SaveData workers,
  registry-controlled tone generator, sample WAV-file recorder, and
  source objects are no longer called or linked into the driver.

The DRM, DMA lifecycle and queue behavior have been compiled by WDK
but **not exercised in a running kernel**. This distinction matters:
a successful WDK compilation cannot guarantee driver stability,
correct buffer positions, or DRM enforcement under Windows audio.

## User-mode bridge is separate

The SharedVirtualAudioBus previously tested with two Portable copies
mixes and broadcasts independent Float32 PCM streams in user mode.
That component is NOT yet connected to the kernel PCM ring:
there is no authenticated device interface, IOCTL dispatch,
kernel-to-user ring transfer, or production audio broker.

The new kernel queue provides internal WaveRT staging, **not** a
usable virtual sound device. A complete bridge needs bounded
METHOD_BUFFERED transport, strict per-request buffer/format validation,
permissions, lifecycle, mixing/fanout semantics, recoverable errors,
and secure management of the one Input and one Output.

The existing driver sample uses PortCls. PcInitializeAdapterDriver
sets its own IRP handlers, including IRP_MJ_DEVICE_CONTROL.
Any future custom IOCTL interface must preserve PortCls dispatch
for real audio devices, use secure access controls (such as
IoCreateDeviceSecure) and avoid exposing arbitrary pointers or
memory mappings. This has not been implemented yet.

Microsoft references:
- https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/portcls/nf-portcls-pcinitializeadapterdriver
- https://learn.microsoft.com/en-us/windows-hardware/drivers/kernel/controlling-device-access
- https://learn.microsoft.com/en-us/windows-hardware/drivers/kernel/buffer-descriptions-for-i-o-control-codes

## Installation helper progress

The project now contains the source for a separate Windows 10 helper
under src/AuralWeave.DriverSetup. It builds and can be published as
a self-contained executable for **developer-only read-only preflight**.

Its --check command verifies Windows PnP and package readiness without
altering Windows. The --install-input/--install-output code is intended
to create exactly one root device per role via documented SetupAPI,
but is **hard-disabled in the current build** by the empty approved
publisher certificate SHA-256 pin and missing signed INF/CAT/SYS.

The AuralWeave Desktop continues to show INSTALL as disabled while
a complete, trusted production driver package does not exist.
Removal is managed by Windows Device Manager, never by the app.
The unsigned helper is not included in dist/ or an installed product.

## Repeatable QA

On Windows 10:

    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-kernel-wavert-wiring.ps1
    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-kernel-transport.ps1
    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-virtual-driver-prototype.ps1
    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-virtual-driver-setup.ps1 -PublishStandalone
    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-windows.ps1 -RunDeviceDiagnostics

Tests include protocol verification, PCM frames and wrap, build-time
two-adapter constraints, removal of sample file/tone paths,
DRM route guard checks, and read-only PnP inventory.
These do NOT install Windows driver code.

Windows 11 is a theoretical compatibility target based on INF/WDK
checks. The actual machine and functional user-mode tests use Windows 10.

## Remaining work / release gate

1. Authorized PortCls-safe kernel control transport and broker;
   buffer size/format negotiation and native user-mode connections
2. Realtime playback/microphone multi-client behavior with
   Windows Audio Engine, clock drift handling, teardown and reconnection
3. Independent Windows 10 installed-driver runtime and stress tests
   on a machine or VM where risk is acceptable
4. Signed driver catalog, verified publisher certificate, production
   installer signature and exact signer pin
5. End-to-end installation, use and Windows Device Manager removal
   verified before enabling INSTALL or packaging a driver for Release

Current source contains the untouched Microsoft-Sample MS-PL code
and new AuralWeave MIT modules. Their licenses remain separate.

## DRM revocation race guard (2026-11-25)

A rights change can race an in-progress audio render callback. The
render stream's per-stream protected flag alone does not guarantee
that a callback which already passed that check will not enqueue PCM
after a flush. Therefore AwKernelPcmDisableOutput now uses the *same*
KSPIN_LOCK as queue writes to atomically (1) latch the entire Virtual
Output role to deny future writes and (2) clear all queued frames.

The deny latch stays set while any stream is attached; it can only
clear on the **first fresh role attach after every existing stream has
detached**. This is deliberately conservative. Any content restriction
during a session may require all render streams to close and reopen
before output mirroring resumes. A later broker must preserve this
security behavior and add rigorous kernel-runtime DRM tests.


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
