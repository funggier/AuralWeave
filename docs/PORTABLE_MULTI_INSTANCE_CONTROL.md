# Multiple Portable AuralWeave Copies and Shared Windows Virtual Audio

**Current target contract:** [VIRTUAL_DRIVER_PRODUCT_CONTRACT.md](VIRTUAL_DRIVER_PRODUCT_CONTRACT.md). The Windows kernel virtual audio driver remains an unsigned, non-installable prototype; the following user-mode mixing tests **do not** prove real Windows audio endpoint multi-client behavior.

## One driver set for the whole computer

Windows will expose at most **one Virtual Input** (microphone / recording) and **one Virtual Output** (speaker / playback). Two device roles may be registered through separate PnP devnodes but share the same AuralWeave driver family.

**All eligible Windows apps may use the endpoints simultaneously via standard Windows Audio APIs.** For example, Discord and OBS both record one virtual mic; Chrome and a game concurrently render to the same virtual speaker through Windows shared-mode mixing. Neither needs the Native PCM test harness or a special AuralWeave IPC handshake. Opening Studio in two or more folders must not create extra devices, require per-app driver installation, or grant one Studio exclusive control of a virtual endpoint.

Studio instance A and Studio instance B keep separate native routing, filters and portable presets, but can contribute to / listen to one machine-wide virtual audio system. This distinction matters:

- **PnP device count:** one Input and one Output per machine.
- **Windows client sessions:** many recording/playback streams per endpoint.
- **Studio audio contributors:** multiple distinct sources into the shared virtual mic mix.
- **Internal IPC mappings:** one SPSC producer + one reader per mapping, with multiple mappings mixed/fanned out by a router; these mappings are **not Windows PnP devices**.

## Implemented developer-only user-mode reference bus

`SharedVirtualAudioBus` is a managed reference mixer/distributor over Native Float32 PCM SPSC mappings. Each source has its own ring; `Pump()` sums contributors and clamps the output. Each listener receives its own FIFO, so a slow reader can lose its own samples without stealing from other listeners.

Windows 10 cross-folder tests passed two independent source processes and two listeners; a source dropping out did not interrupt surviving audio, and owner-lifetime handles enabled cleanup after an abrupt process exit. The current reference hub has internal bounded limits (currently 16 source and 16 listener slots); **these are implementation resource bounds, not device-count limits or restrictions on Windows applications.**

The reference bus is not a Windows Audio Driver, an always-on production service, or a way for Discord/OBS to see virtual endpoints. It has not yet been integrated with a signed WaveRT device.

## Locks are for administration, never for owning the audio endpoint

An earlier prototype held a Global role lock for an entire audio session and prevented simultaneous Studio use. This was **superseded** by short-lived administration coordination.

- An installation mutex serializes competing install operations from different Studio folders; it is released after the PnP transaction.
- Cross-process audio activity leases can be used as advisory lifecycle signals but do not authorize or monopolize audio endpoints.
- A test-only Mock Broker (v0.3.0) compares the OS pipe client PID and advisory native PCM mapping creator PID to reject accidentally registered **foreign internal mappings**. This is not a Windows audio client restriction, nor a privileged security boundary.
- For a real kernel broker, independently authenticated permissions must be designed. Do **not** reuse user-writable PCM metadata as privileged authorization.

## Driver management from all Portable folders

Every Studio window uses Windows SetupAPI to detect the same system-wide PnP installation (hardware ID, class, service, legacy and partial nodes). If Input/Output already exists, **no duplicate Install action is available**. Where a role is missing, the user may explicitly install from any Studio if and only if a signed production package is available, with rechecks under a mutex and after UAC.

Studio contains **no driver Uninstall/Remove/Repair**. Users can remove devices with Windows Device Manager. A Windows **Installed apps / Apps & Features** entry will exist only if a future installer registers a tested uninstaller; INF-only deployment does not create one automatically. See [Install-only design](VIRTUAL_DRIVER_INSTALLATION.md).

## Verified Windows 10 user-mode cases

`scripts/test-multiple-portable-instances.ps1` runs Diagnostics from two real folders and tests: brief per-role control locks; concurrent audio owners; two independent PCM sources mixed into one signal received by two independent listeners; contributor removal; short blocks; clipping and slow-reader isolation; proper release of advisory activity markers.

Separate native/WASAPI and Mock Broker diagnostics cover real Windows child processes, restart, broken protocol handling and owner-PID *mapping registration*. None of these tests imply a signed driver, external app device-sharing acceptance or installed Windows 11 functionality.

## Remaining production verification

- Real virtual mic and speaker visible in Windows Sound Settings after a signed installation.
- Two independent **non-AuralWeave** apps capturing from the same virtual mic.
- Two independent apps playing concurrently to the same virtual speaker and listening through AuralWeave Native route.
- Two simultaneous Studio folders contributing to and consuming the real shared device.
- Correct clock drift, stream cancellation, hotplug, restart, audio permission behavior and silence on missing sources.
- A second Studio racing an Install must not create another PnP node.

See [Product Contract](VIRTUAL_DRIVER_PRODUCT_CONTRACT.md), [Test Plan](TEST_PLAN.md) and [Roadmap](ROADMAP.md).
