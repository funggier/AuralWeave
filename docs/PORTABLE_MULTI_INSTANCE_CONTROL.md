# Multiple Portable Copies and Shared Virtual Audio

Status: Windows 10 user-mode implementation and tests. The Windows kernel
virtual audio driver is still an unsigned, noninstallable prototype.

## One installed device per role, shared by all programs

AuralWeave targets **at most one Virtual Input** (recording device) and
**at most one Virtual Output** (playback device) in the entire Windows
installation, regardless of how many folders contain AuralWeave.

- Input: ROOT\AURALWEAVE_VINPUT_0001
- Output: ROOT\AURALWEAVE_VOUTPUT_0001

This is a limit on **Windows PnP devices**, NOT the number of programs,
capture streams, playback streams or Portable copies that may use them.

Each Portable Studio uses its own Filters, hardware outputs, local
presets and settings. Participating programs can contribute audio
to a shared mix and obtain independent copies of that audio.

## User-mode audio distribution implemented

SharedVirtualAudioBus is a managed reference mixer/distributor built
on the native Shared PCM bridge. Audio contributors and listeners each
have a separate Float32 SPSC ring. Pump() sums the available source
frames, clamps them to [-1,+1] and sends the resulting frames to each
listener's independent ring. One subscriber reading slowly cannot
consume another subscriber's frames or block their progress.

The current developer-only bus is explicit/caller-driven. It is not
yet a persistent automatic service for general Windows applications
and is **not connected to the unsigned WaveRT kernel driver**.
Clients must agree on format/cadence. The current reference bus has
16 source and 16 listener slots, independent of the one-Windows-device
limit. Production must add clock reconciliation, hot-plug, reconnect,
low-latency native mixing and secure per-client authorization.

## Administrative coordination versus audio sharing

Earlier prototypes acquired a machine-wide mutex for the **entire**
audio session, preventing two AuralWeave copies from controlling the
same role. That behavior has been superseded.

VirtualDeviceControlLease now only briefly coordinates device
management and new audio-session creation, with a bounded wait at
startup. It is NOT held while streams play or record. Audio sessions
can be created concurrently in independent Portable folders.

Each owner session holds a Windows-named
SharedVirtualAudioActivityLease handle while active. The named object
remains present until the last cooperating owner closes it, even
across different app folders. This is an advisory safeguard for future
driver management, **not an OS-level kernel authorization boundary**.
The Windows user and driver security model must be authoritative.

## Install-only UI; Windows-managed uninstall

The Virtual Driver screen contains two cards and **no Uninstall button**.
It enumerates reserved AuralWeave PnP identities using Windows SetupAPI:

- If the correct or legacy identity exists, its device is listed and
  installing another of that role is blocked.
- A partially installed device ID also blocks duplicate installation,
  with status explaining that Windows Device Manager needs attention.
- If no device exists, the Install button is displayed.
- The Install button becomes enabled only when a complete, trusted,
  production driver package is available in the Portable folder.
  The unsigned WDK prototype does not qualify.

The installer control is prepared to request Windows administrator
approval and invoke a separate **signed production setup program**;
such a setup binary and signed kernel driver have **not** yet been
produced, and the button therefore remains disabled in the current
build. No test-signing, Secure Boot changes or unsigned kernel
installation are performed.

The user can uninstall the device later through **Windows Device
Manager**. Removing the PnP device and deleting its driver package
from the Windows Driver Store are separate operations; Windows
handles them, not AuralWeave. If a future installed product also
registers an entry in Windows Installed Apps, its own installer would
need to provide that entry and uninstaller.

See docs/VIRTUAL_DRIVER_INSTALLATION.md for preflight and distribution.

## Tested on Windows 10

Run the script:

    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-multiple-portable-instances.ps1

The test runs two different copies of Diagnostics from two actual
directories and verifies that:

1. Administration locks are mutually exclusive per role, independent
   between Input and Output, and cleanly released.
2. Audio owners may coexist in both roles without that exclusivity.
3. Two different source processes contribute PCM (0.25 and 0.40),
   and two distinct subscribers each read the same mixed 0.65 signal.
4. A contributor leaving does not interrupt other participants.
5. Short 37-frame blocks and clipping (0.8 + 0.8 -> 1.0) behave as intended.
6. A slow listener drops only its own buffered data.
7. Session activity remains visible across app folders until the last
   owner closes, at which point the shared activity marker disappears.

The separately run Windows 10 native WASAPI post-filter diagnostics
were retained. The signed Windows Driver installation and WaveRT
transport are **not** runtime-tested. Windows 11 remains a theoretical
API/INF compatibility target, not a tested platform.

Reference: Microsoft Learn, Using Device Manager to Uninstall Devices
and Driver Packages.
https://learn.microsoft.com/en-us/windows-hardware/drivers/install/using-device-manager-to-uninstall-devices-and-driver-packages
