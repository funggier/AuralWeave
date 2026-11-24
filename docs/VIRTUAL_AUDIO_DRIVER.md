# Virtual Audio Driver — Future Optional Module

## Objective

Expose virtual Windows audio endpoints so other applications can send audio to AuralWeave and consume its processed output.

Windows endpoint naming from the **user perspective**:

- Virtual Playback endpoint: other software chooses it as an output; AuralWeave receives that stream as an input
- Virtual Recording endpoint: other software chooses it as a microphone; AuralWeave provides the processed stream

This directionality is important to avoid naming confusion.

## Strategy

1. First validate routing with physical WASAPI capture/render and Windows loopback endpoints.
2. Develop a signed/installed optional driver separately from the portable application.
3. Evaluate the Microsoft SysVAD sample as a development reference, **not** a ready-to-distribute complete audio router.
4. Design IPC/ring-buffer boundaries between user-mode core and optional virtual endpoints.
5. Validate install/uninstall, audio path, Windows 10 compatibility, endpoint visibility, signature requirements, reboot/restart behavior, and security.
6. Keep driver optional. Existing audio routing must operate without it.

## Constraints

Driver installation/removal may require elevation and the applicable Windows driver signing/distribution process. The driver's binaries/configurations cannot be considered relocatable after OS installation the same way as the portable app.

Do not install, disable, or replace other audio drivers without an explicit separate user action. Before reusing upstream driver sample code, verify and retain its applicable notices/license.
