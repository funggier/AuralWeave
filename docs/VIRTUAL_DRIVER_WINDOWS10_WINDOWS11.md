# Windows 10 + Windows 11 Virtual Audio Support

## Compatibility target

| Target | Build family | Current engineering status |
| --- | --- | --- |
| Windows 10 x64 22H2 | 19045 (19041 API baseline) | INF target + compile/signability checks; no VM/runtime test |
| Windows 11 x64 21H2 | 22000 | INF target + compile/signability checks; no VM/runtime test |
| Windows 11 x64 22H2 / 23H2 | 22621 / 22631 | INF target + compile/signability checks; no VM/runtime test |
| Windows 11 x64 24H2 and newer | 26100+ | INF target + compile/signability checks for 24H2 family; no runtime test |

The kernel implementation was derived from Microsoft's **Simple Audio
Sample** and is **MS-PL** licensed. The portable app and original
AuralWeave code remain MIT; the source under
`src/AuralWeave.VirtualDriver/Kernel` has its own
`LICENSE.MS-PL.txt` and cannot be re-licensed under MIT.

## Current implementation

- `AuralWeaveVirtualAudio.sln` compiles on VS 2026 + WDK 28000.
- One INF maps one virtual recording PnP identity and one
  virtual playback PnP identity to distinct role-specific
  installation/interface sections; a single kernel service backs them.
- Kernel `StartDevice` compares the root PnP hardware identity against
  the permitted four-digit IDs and registers only the corresponding
  KS miniport pair. No device is installed when the hardware ID
  is not one of the reserved identities.
- The GUI discovers installed devices only if **device class MEDIA,
  service name AuralWeaveVirtualAudio and reserved hardware ID**
  all match. It remains relocation-independent.
- UI INSTALL stays disabled because the signed and tested audio
  transport to the Native Core does not exist yet.

## Validation gates

1. Build with `scripts/build-virtual-driver-prototype.ps1`.
2. Check `InfVerif /w` and `/u`: both must report no errors.
3. Check `Inf2Cat /nocat` on Windows 10 and 11 targets: must pass.
   This is a signability test, **not Microsoft signing**.
4. Implement actual PCM transport with per-instance buffer isolation.
5. Test the INF, audio endpoints, stream quality, device removal,
   low latency and concurrency in throwaway Windows 10/11 VMs.
6. Run suitable Windows HLK tests and submit through the Microsoft
   Hardware Dev Center for the applicable signing requirements.
7. Only after all checks pass, enable Install/Uninstall from the
   app and prepare a separately installed driver package.

No forced test-signing, Secure Boot disabling or changes to boot
security settings are part of the normal product workflow.

Official references:
- https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/wdm/nf-wdm-exallocatepool2
- https://learn.microsoft.com/en-us/windows-hardware/test/hlk/
- https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/driver-signing-offerings
- https://learn.microsoft.com/en-us/samples/microsoft/windows-driver-samples/simple-audio-sample-device-driver/
