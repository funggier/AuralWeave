# Virtual Audio Driver — Manager UI and Future Kernel Package

## Current status: v0.3.0-preview.1

The Native C++ studio and virtual-driver **management UI** are separate concerns. The management page is now implemented, but the **actual virtual audio kernel driver is not**.

The UI contains **two cards on one page**:

| UI card | Windows endpoint type | Direction from a Windows application |
| --- | --- | --- |
| Virtual Input | Recording endpoint (virtual microphone) | AuralWeave provides audio that the app receives |
| Virtual Output | Render endpoint (virtual speaker) | The app plays audio into AuralWeave |

This distinction is based on Windows' endpoint types, not internal WASAPI source/target terminology.

## Discovery independent of executable path

The manager uses Windows SetupAPI PnP enumeration and reads hardware IDs, device instance IDs and friendly names. It **does not** look only in the app's folder and **does not** treat ordinary devices containing the string "AuralWeave" as owned.

Reserved future root-enumerated hardware ID families:

- `ROOT\AURALWEAVE_VINPUT_####`
- `ROOT\AURALWEAVE_VOUTPUT_####`

The exact INF packages must match these IDs before any signed distribution or installation; no driver is presently installed or bundled.

The manager lists every installed matching instance and offers a per-device removal action. Removal first rechecks ownership, requests explicit confirmation, and invokes Windows' PnPUtil on that **one instance** with UAC. This removes that device node; it does not automatically delete a potentially shared OEM driver-store package.

## Why Install is disabled

A functional virtual audio device requires more than an INF file: a compiled signed driver package with correct endpoint descriptors, Windows 10 compatibility, and a verified audio transport/IPC path to the AuralWeave core. Microsoft SysVAD is a *reference implementation*, not a bundled AuralWeave driver. The Windows SDK alone is not proof that the WDK build tools and signing pipeline are available.

The current UI deliberately does not provide a fake install action. Never use `pnputil /add-driver` alone as evidence that an independent ROOT-enumerated virtual endpoint was created; that command primarily registers a driver package and installs it on matching existing devices.

## Engineering work remaining

1. Build AuralWeave-owned kernel audio miniport(s) for virtual recording and playback endpoints, based on appropriately licensed Windows driver reference code.
2. Design authenticated per-instance IPC shared memory / ring buffers between user-mode audio core and driver, with bounded latency, clock handling and security boundaries.
3. Support creating additional independent instances with fixed signed hardware IDs. Ensure no cross-instance audio leakage and stable device naming.
4. Build and sign driver INF/SYS/CAT packages with appropriate Windows 10 kernel signing rules. Never disable secure boot, disable driver-signature enforcement or turn on test signing as part of normal application operation.
5. Add verified install flow (UAC, valid INF and signature, installed endpoint check, reboot handling), plus per-device uninstall and optional separate package cleanup only when no instances rely on it.
6. Test clean install/uninstall/reboot, multi-instance performance, endpoint visibility, migration after moving the portable folder, and security.

The existing physical input/output Native C++ route must continue working when this optional module is absent.

## Toolchain preparation completed

VS 2026 WDK integration, WDK/SDK build 28000, Inf2Cat, PortCls
and WIL are installed/available. Reference-only SysVAD compilation
passes through `scripts/build-sysvad-reference.ps1`.

**Still not implemented:** AuralWeave-branded, signed and installable
virtual audio kernel endpoints; production Windows 10 INF;
per-instance full-duplex audio transport and multi-instance
device management. The Manager INSTALL controls remain disabled
until those pieces are actually built and tested.

## Windows 10 + 11 x64 kernel prototype (2026-11-25)

A separate **compile-only** prototype now lives under
`src/AuralWeave.VirtualDriver/Kernel`, adapted from Microsoft's
MS-PL Simple Audio Sample. The binary is called
`AuralWeaveVirtualAudio.sys` and the INF separates 16 possible
Virtual Input hardware IDs from 16 possible Virtual Output IDs.
The kernel compares the physical PnP hardware ID before registering
only the requested WaveRT endpoint direction.

**Validation passed:** WDK driver compilation; InfVerif Windows
and Universal INF checks; Inf2Cat signability for representative
Windows 10 and Windows 11 x64 targets. No Windows 11 VM runtime,
HLK or Microsoft driver-signing results yet.

This is a substantial engineering prototype, NOT a working virtual
microphone/speaker product. There is no user-to-kernel PCM transport,
multi-instance stream isolation has not been tested, and INSTALL
remains intentionally disabled. The earlier "not implemented"
statements above describe the previous milestone.

See [Windows 10/11 target matrix](VIRTUAL_DRIVER_WINDOWS10_WINDOWS11.md)
and [Kernel prototype README](../src/AuralWeave.VirtualDriver/Kernel/README.md).

## User-mode PCM transport checkpoint (2026-11-25)

A second implementation stage now runs inside the Native C++ audio
engine: Studio can optionally publish its *post-filter* PCM to a
separate virtual-input Shared Memory ring, while its physical
outputs keep working. Float32 mono/stereo rings are per-instance,
bounded, interprocess, and expose overflow/underrun counters.
Managed developer APIs and a stable C ABI are available.

The opposite shared Virtual Output channel passes interprocess
tests. It is **not** wired into a real WaveRT render device or
Studio source selection. The Windows 10 muted route diagnostic
demonstrated 192-kHz capture -> filters -> 48-kHz bridge with
zero dropped frames in the tested continuous-consumer interval.

Kernel-side audio transfer, Windows 11 runtime verification,
signed driver installation and fully isolated Windows endpoints
are still pending. GUI Install remains disabled.

See [Virtual PCM Bridge](VIRTUAL_PCM_BRIDGE.md).
