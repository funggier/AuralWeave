# Virtual Driver: Install-Only, One System-Wide Set

**Authoritative contract:** [VIRTUAL_DRIVER_PRODUCT_CONTRACT.md](VIRTUAL_DRIVER_PRODUCT_CONTRACT.md). This document describes intended production installation behavior, **not an already working signed installer**.

## Intended user experience

AuralWeave Studio is Portable and can be launched from any number of folders. Windows virtual audio devices are installed **per machine**, not per Portable copy.

One installed set consists of:

- **AuralWeave Virtual Input** — one Windows recording endpoint, hardware ID `ROOT\AURALWEAVE_VINPUT_0001`.
- **AuralWeave Virtual Output** — one Windows playback endpoint, hardware ID `ROOT\AURALWEAVE_VOUTPUT_0001`.

The two PnP device nodes may share one driver service and package. They must be available to other Windows programs at the same time through Windows Audio Shared Mode. **No AuralWeave window claims exclusive ownership of either audio endpoint**.

The Studio **Virtual Driver** page displays current status for both roles. The only driver action is **Install**, and it is available solely for a role that is absent when a signed production package exists.

| Windows PnP inventory | Studio behavior |
| --- | --- |
| Neither role installed | Offer install for the missing set (both roles) when signed/setup-ready |
| Only Input installed | Show Input installed; permit install of missing Output only |
| Only Output installed | Show Output installed; permit install of missing Input only |
| Both installed | Status only; no install button or duplicate creation |
| Legacy ID, incomplete/broken install or conflicting ownership | Show explicit state, block duplicate install and point to Windows for diagnosis; no auto repair/removal |
| Package absent, unsigned or unverifiable | Disable Install and show why |

**No Uninstall, Remove, Repair, Delete, PnPUtil removal or additional device-count controls appear in Studio.** Moving/copying AuralWeave does not install again. All windows read current PnP state and refresh it after a completed install or Windows device change, without busy polling.

## Installation concurrency and security gates

A signed production setup helper (run only after the user's explicit Install request) must:

1. Validate the exact intended driver package and publisher/signature before UAC.
2. Acquire a **cross-process installation-only** mutex; never hold it while audio streams play.
3. Recheck all current, legacy and partial/failed PnP instances **after privilege elevation and after acquiring the mutex**, plus verify class, hardware ID and driver service.
4. Create the missing root-enumerated device node(s) only; never duplicate an existing role. INF registration alone does not create a root-enumerated PnP device.
5. Verify each newly installed endpoint in PnP/Windows Audio; handle pending reboot safely, and roll back only nodes created by the current installation attempt if safe.
6. Tell all Studio copies the authoritative resulting PnP state through device-change notifications / controlled refresh (not by assuming the installer succeeded).
7. Never change Secure Boot, driver-signing policy, Test Signing or unrelated Windows driver registrations.

The driver itself must support **Windows Audio Shared Mode multi-client use**, independent of which process launched Setup or Studio. Do not apply the developer-only PCM mapping Process ID check to ordinary Windows audio application sessions; it is not a valid privileged authorization boundary.

## Windows-managed uninstall, outside Studio

Windows **Device Manager → Uninstall device** is the system-supported path for removing each AuralWeave PnP device. Removing a device node and removing its signed package from the **Driver Store** are distinct operations.

The user's preferred conventional **Windows Uninstaller / Settings → Installed apps (Apps & Features)** is a separate *production packaging requirement*: for a clickable entry named, for example, `AuralWeave Virtual Driver`, the eventual signed installer must register **one** Windows app-uninstall entry for the complete AuralWeave driver set and supply a tested uninstaller. That entry would remove the AuralWeave Input and Output nodes only after handling active audio sessions safely, followed by package cleanup as permitted. **An INF-only installation does not automatically appear in Installed apps.** Until such an uninstaller is actually implemented, the documentation must direct the user to Device Manager rather than claim Windows Installed Apps uninstall is ready.

Studio must not expose that uninstaller or silently uninstall a driver when a Portable folder is deleted.

Official Microsoft references:
- https://learn.microsoft.com/en-us/windows-hardware/drivers/install/using-device-manager-to-uninstall-devices-and-driver-packages
- https://learn.microsoft.com/en-us/windows-hardware/drivers/install/how-devices-and-driver-packages-are-uninstalled
- https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/devcon-migration

## Proposed future Portable package

```text
AuralWeave/
  AuralWeave.exe
  AuralWeave.Native.dll
  drivers/
    AuralWeaveVirtualAudio/
      AuralWeaveVirtualAudioSetup.exe       # signed, future
      AuralWeaveVirtualAudio.inf            # signed package, future
      AuralWeaveVirtualAudio.sys            # signed kernel driver, future
      AuralWeaveVirtualAudio.cat            # trusted catalog, future
```

In addition to the signed setup, a Windows Installed Apps uninstall entry needs a **properly installed** uninstaller: a Portable-only EXE path is insufficient if users can relocate/delete the Portable folder later. The final package must resolve this lifecycle without forcing Studio itself to be installed. A machine-installed uninstaller or dedicated installer-owned safe removal mechanism is a candidate; design and test it before claiming support.

The unsigned WDK binary currently available in the ignored build area is *not* a release-ready device driver. The official Studio ZIP must remain driver-free until signed setup QA is complete.

## Current implementation evidence (2026-11-25)

- Windows 10 SetupAPI lookup checks exact/legacy/partially installed IDs.
- Concurrent user-mode PCM sources/listeners and two Portable folders pass mock tests.
- `src/AuralWeave.DriverSetup` contains a future SetupAPI helper. Read-only preflight is tested. Its production publisher certificate pin is blank, so **actual installation is disabled**.
- WaveRT kernel/PCM/IOCTL stages are compiled/mock-tested but **not installed**, not signed, and not proven with real Windows audio clients.
- Windows 11 is an untested runtime target.

See [Product Contract](VIRTUAL_DRIVER_PRODUCT_CONTRACT.md), [Portable sharing](PORTABLE_MULTI_INSTANCE_CONTROL.md), and [Handoff](VIRTUAL_DRIVER_HANDOFF_2026-11-25.md).
