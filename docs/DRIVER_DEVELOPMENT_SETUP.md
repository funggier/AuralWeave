# AuralWeave: Windows Driver Development Environment

## Toolchain

AuralWeave's Native C++ WASAPI studio does **not** require a kernel driver.
The optional virtual input/output endpoint driver is a separate development
track, targeting Windows 10 x64 as well as future supported Windows releases.

- Visual Studio: Visual Studio Community 2026 (v18), MSVC x64 toolchain
- Windows SDK build: `10.0.28000.0`
- Windows Driver Kit (WDK): `10.0.28000.2526`
- Microsoft driver headers/libraries used for audio: `wdm.h`,
  `ntddk.h`, `portcls.h`, `portcls.lib`, `ntoskrnl.lib`
- Tools: `Inf2Cat`, `stampinf`, `signtool`, MSBuild

The official Windows WDK is installed using the Microsoft WinGet
package `Microsoft.WindowsWDK.10.0.28000` and the matching
SDK 28000 already on the machine. Since WDK installation registers
system-wide build tools, the Windows Kits folder resides under
`C:\Program Files (x86)\Windows Kits\10`.

For additional portable-friendly automation, the NuGet package
`Microsoft.Windows.WDK.x64 10.0.28000.2526` and its matching
Microsoft SDK dependencies were restored to the **ignored** project-local
cache at `build/nuget-packages` on drive T:. Source code should
use a deterministic version and avoid redistributing Microsoft's
WDK binaries inside the AuralWeave Portable ZIP.

Run `scripts/check-driver-toolchain.ps1` to verify both installed and
project-local kernel development files and Visual Studio driver project
integration. Add `-Strict` to make missing required components fail a
toolchain check.

## VS integration

The relevant Visual Studio Installer component ID is
`Component.Microsoft.Windows.DriverKit`, matching Microsoft's
published `wdk-desktop.vsconfig`. It must be installed in an
Administrator-elevated Visual Studio Installer operation.

For a Visual Studio Community 2026 installation, modify the
existing installation and add this component. This is separate
from WDK libraries and headers; if missing, IDE driver templates and
project integration may not work even if kernel headers are present.

## Microsoft SysVAD baseline (reference only)

A sparse checkout of Microsoft's `Windows-driver-samples` repository
is available under the ignored `build/sysvad-reference` directory.
The reference revision is pinned to:

`d49bb7d72f3651952b2b6e4ad28d14fe2247eb69`

The sample's license is **Microsoft Public License (MS-PL)**.
It is not MIT code. Do not vendor or relabel sample source as MIT,
or ship sample driver binaries, without reviewing and preserving
the sample's terms and copyright notices.

The upstream sample's INF and endpoint descriptors may target a
newer Windows version and may expose endpoints different from the
intended AuralWeave Virtual Input/Output devices. Building the upstream
sample without changes does **not** produce a supported AuralWeave
driver or a working bridge to Studio.

## Kernel-driver boundaries

Do not enable Windows test-signing, disable Secure Boot or bypass
kernel signature enforcement as a general setup step. A functional
virtual driver package must be built, validated, appropriately signed
and tested on a disposable development machine or VM before normal
users may install it. Installing the WDK and VS integration alone
does not create any virtual microphone or playback device.

The public Portable ZIP must continue to run without this toolchain,
or any kernel driver, until the optional virtual driver is built and
distributed as a separately installed Windows component.

## Verified toolchain checkpoint (2026-11-25)

The official `Microsoft.WindowsWDK.10.0.28000` package was successfully
installed with WinGet (WDK 10.0.28000.2526). Visual Studio Community 2026
was successfully modified to include
`Component.Microsoft.Windows.DriverKit`, using standard UAC elevation.

Additionally, a matching NuGet WDK was restored under the project's
ignored `build/nuget-packages` folder on drive T. The pinned Microsoft
Windows-driver-samples SysVAD reference was restored into the ignored
`build/sysvad-reference` folder, including the pinned Microsoft WIL
submodule at
`3c00e7f1d8cf9930bbb8e5be3ef0df65c84e8928`.

Verification:

- `scripts/check-driver-toolchain.ps1 -Strict`: **PASS**.
- `scripts/build-sysvad-reference.ps1`: **PASS**, compiled the
  Microsoft sample solution (kernel SYS, related libraries and APO DLLs).
- Produced sample `TabletAudioSample.sys` (240128 bytes). This is only
  a development reference and is **not installed**.
- No BCDEdit test-signing changes, driver-store installation, signed
  catalog generation or Windows security bypasses were performed.

The pinned sample uses more recent OS-specific INF package conditions,
and the toolchain verification intentionally disables INF validation,
API validation and catalog/signing. A successful reference build does
not make it installable or demonstrate Windows 10 compatibility.

Known baseline issue: regular SysVAD packaging validation on this host
does not complete (InfVerif/ApiValidator tool execution). Resolve
and pass full validation **before** creating any distributable AuralWeave
driver package. The compile-only script must not be used as a driver
release or installer script.
