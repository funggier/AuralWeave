# Virtual Driver: Install-Only Management

AuralWeave's Virtual Driver mode is an **installer/status viewer**, not
a Windows driver uninstaller. The current Windows 10 development host
does not have the production AuralWeave driver installed.

## Desired production behavior

- **Virtual Input** and **Virtual Output** are each limited to ONE
  Windows PnP device for the whole computer.
- Many running AuralWeave Portable copies and ordinary Windows audio
  clients may share those devices. Shared audio is not a device-count
  limit.
- Refresh checks Windows SetupAPI for current, legacy, and partially
  bound AuralWeave PnP IDs. Existing IDs prevent duplicate creation.
- INSTALL is only offered for a role if no such device ID exists. A
  missing or untrusted production package keeps the button disabled
  with a readable explanation.
- Only a trusted production installer can initiate driver creation,
  with Windows UAC. It must recheck the exact PnP ID after elevation.
- The application provides **no UNINSTALL button or PnPUtil removal
  command**. Use the Windows Device Manager interface for uninstall.

## Expected future Portable production package

The portable executable locates the package relative to itself:

    AuralWeave.exe
    AuralWeave.Native.dll
    drivers/
      AuralWeaveVirtualAudio/
        AuralWeaveVirtualAudioSetup.exe
        AuralWeaveVirtualAudio.inf
        AuralWeaveVirtualAudio.sys
        AuralWeaveVirtualAudio.cat

The setup executable and catalog must pass Windows Authenticode trust
verification, including a publisher identity for the setup executable.
A missing file or failed signature keeps INSTALL disabled. The
separate elevated installer must validate the full INF/SYS/catalog
relationship and perform the root-enumerated device registration.
Simply adding an INF with PnPUtil does NOT create a new root device.

The GUI's deployment call is prepared to pass either --install-input
or --install-output to that independently signed setup executable,
request administrator approval and verify that the requested role
appears in Windows PnP after it exits.

**IMPORTANT: The production setup executable does not yet exist and
the present AuralWeaveVirtualAudio.sys is unsigned and has no complete
kernel WaveRT PCM transport. This capability must remain disabled until
that driver is implemented, runtime-validated, and appropriately signed.**
The unsigned WDK prototype stored in the ignored build directory is
never copied into a user-installable driver bundle or portable release.

This installation workflow does not use Internet downloads, disable
Secure Boot, turn on Windows Test Signing or modify boot security
settings. Windows 10 is the actual test baseline. Windows 11 is a
theoretical target only.

## Windows-managed removal

On Windows 10, use Device Manager to locate the AuralWeave audio
device, right-click, and choose Uninstall device. Windows may
separately offer to remove the driver software/package. Uninstalling
a devnode and removing a package from the Driver Store are different
operations. The exact available UI depends on the installed package.

Windows Installed Apps (Apps & Features) only offers an uninstall
entry if an application/driver installer has registered one. A raw
PnP INF does not automatically become an Installed Apps entry.

Official Microsoft references:
- https://learn.microsoft.com/en-us/windows-hardware/drivers/install/using-device-manager-to-uninstall-devices-and-driver-packages
- https://learn.microsoft.com/en-us/windows-hardware/drivers/install/how-devices-and-driver-packages-are-uninstalled
- https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/devcon-migration

## Automated Windows 10 regression

- User-mode multi-client PCM mix and broadcast: passed with two Portable
  copies, two contributors and two independent listeners in each role.
- Existing/legacy/partially bound PnP IDs: installation denied.
- Missing signed package: installation disabled.
- Signed package / administrator installer: not available, not tested.
- WaveRT kernel transport and installed driver runtime: not implemented.

No driver install/uninstall operation was performed on the user's PC
during these tests.
