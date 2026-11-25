# Roadmap

The repository now includes working prototypes; boxes below distinguish fully proven milestones from remaining hardening. See [Implementation status](IMPLEMENTATION_STATUS.md) for test evidence.

## Phase 0 — Repository foundation

- [x] Establish product name AuralWeave
- [x] Project directories
- [x] Requirements, architecture, UI, routing, effects, driver, testing docs
- [x] MIT license and third-party notice policy
- [ ] Configure CI after implementation build/test tooling is selected

## Phase 1 — Minimal functional audio route

- [x] Native C++/WASAPI capture and render proof of concept (muted hardware smoke)
- [x] Device enumeration and stable endpoint identity (prototype on Windows)
- [x] Start/Stop one .NET/WASAPI route with one input and one muted physical output (smoke test)
- [x] Basic WPF UI and input meter/status (prototype)
- [ ] Media Foundation format adaptation and bounded buffering prototyped; full conversion matrix and stability not yet verified
- [ ] Recovery from stop/disconnect, measured performance

## Phase 2 — Multiple simultaneous outputs

- [x] Add/remove output controls with per-route uniqueness (prototype)
- [ ] Prototype fan-out and per-output gain/mute implemented; real multi-output hardware smoke and drift handling remain
- [ ] Per-device clock drift/underrun handling
- [ ] Hotplug and per-output status
- [ ] Regression tests for every dropdown edge case

## Phase 3 — Noise and effects

- [ ] Noise processing selector: Bypass/RNNoise
- [ ] Frame adapter and RNNoise quality tests
- [x] Ordered Gain effect instances, repeated types and independent gain settings (prototype)
- [ ] Enable/bypass/reorder/remove implemented for Gain; concurrent hot reconfiguration safety and DSP engine plugin ABI require hardening
- [ ] Actual EQ as first configurable built-in effect
- [ ] Preset save/load round-trips

## Phase 4 — Multi-route polish

- [ ] Concurrent independent routes
- [ ] Global and per-route controls
- [ ] Portable release structure, build/release tooling
- [ ] Robust diagnostics, accessibility/localization, integration tests

## Phase 5 — Optional expansion

- [ ] Additional noise engines and effect plugins
- [ ] Voice processing / optional AI processors
- [ ] Virtual playback and recording driver with installer and tested signing
## Native C++ implementation checkpoint

- [x] Detect and invoke installed MSVC toolchain without requiring `cl.exe` globally on PATH
- [x] C++20 native RouteGraph: unique outputs, ordered repeated-effect instances and Gain processor
- [x] Core Audio endpoint discovery with Windows SDK; native non-audible smoke test
- [x] Native C++ WASAPI streaming prototype and optional WPF backend interop (hardening pending)

## Dark UI and buffering checkpoint (2026-11-24)

- [x] Dark ComboBox popup/selected/disabled/button templates
- [x] Single Start/Stop control
- [x] Native per-output render events, shorter bounded queues and underrun telemetry
- [x] Managed shorter queue, actual queue telemetry, direct-format path
- [x] 30-second muted native mixed-rate hardware soak; no post-warmup overruns or underruns
- [ ] Audible latency measurement, filtering quality and long-duration independent clock drift correction

## Native-only consolidation (2026-11-24)

- [x] Use C++ WASAPI as the sole audio engine
- [x] Move Core Audio endpoint discovery and mix-format reporting to native C ABI
- [x] Remove managed NAudio streaming and the backend selector
- [x] Build/Portable remains in project; no automatic copy to test Products folder
- [ ] Formal listening/latency and cross-device drift acceptance tests

## RNNoise processing milestone — local preview

- [x] Vendor pinned RNNoise v0.1.1 Standard model + BSD-3-Clause notice
- [x] Native C++ RNNoise frame adapter for 48/192 kHz and mono/stereo
- [x] Noise dropdown: Bypass vs RNNoise, including runtime switching
- [x] Preserve pure Bypass as default with no extra denoising DSP
- [ ] High-fidelity sample-rate conversion, audible QA and timing measurement
- [ ] Support compatible Little / Custom RNNoise models after source/ABI/licensing review

## Monochrome UI / icon milestone (2026-11-24)

- [x] Grayscale transparent icon and seven-size Windows .ico embedded in EXE
- [x] Monochrome Dark + Graphite WPF skin resource dictionaries
- [x] Skin switching with local portable appearance setting
- [x] Redesigned Sound Input → Filters → Sound Output UI, per-Gain collapse and Collapse all/Expand all
- [ ] Fully generic re-orderable native filter chain with repeatable RNNoise, Noise Gate, Limiter, EQ and other modules

## v0.2.0-preview.3 — ordered filter / themed window checkpoint

- [x] Custom dark WindowChrome title bar and resize system behavior
- [x] Styled ScrollBar thumb/track and Slider using theme resources
- [x] START/STOP single toggle, monochrome shades
- [x] Empty Filters section with centered + button / Choose filter... slots
- [x] Real native ordered filter graph, duplicate RNNoise/Gain/Gate/Limiter instances, moves and bypass
- [x] WPF ComboBox labels begin with endpoint Name, with {metadata} and no raw ID
- [ ] Formal listening/latency/clickless graph-swap tests and CPU benchmarking
- [ ] More filter types, independent per-stage advanced UI, state-preserving transitions

## v0.2.0 release (2026-11-24)

- [x] Ship AN silver/gray ribbon logo with transparent background in program EXE and PNG
- [x] Default newly added outputs to unmuted; newly added filter slots enabled
- [x] Verify ZIP contents, launch, native/managed tests and release documentation
- [ ] User listening checks with microphone + speakers at safe volumes
- [ ] Quantify per-filter CPU use and actual capture-to-output latency
## v0.2.1 Maintenance Release (2026-11-24)

- [x] Fix custom WindowChrome maximize geometry to respect monitor Work Area
- [x] Ensure close/minimize/restore remain visible with top-docked taskbar
- [x] Verify multi-monitor maximize/restore, minimize and close (3 displays)
- [x] Add repeatable `scripts/test-window-workarea.ps1` regression test
## v0.3.0-preview.1

- [x] Mode selector (Studio / Virtual Driver) and two Virtual Input/Output cards in a single driver-manager page
- [x] Windows PnP discovery of AuralWeave-owned device IDs after relocation
- [x] Guarded per-device removal UI (confirmation + UAC); no arbitrary PnP modifications
- [x] Portable Filter Preset Save/Load/Import/Export, without source/output persistence
- [x] Increased spacing in mode buttons, preset controls, and two Virtual Driver cards
- [ ] Build and sign actual virtual audio kernel drivers, create independent virtual endpoint instances, implement install flow
- [ ] Design real-time single-input/output driver-to-native-core IPC and audio clock handling

## Virtual Driver toolchain checkpoint (2026-11-25)

- [x] Install matching Windows SDK and WDK build 28000
- [x] Install Visual Studio 2026 WDK integration
- [x] Restore pinned Microsoft SysVAD + WIL source for reference
- [x] Compile reference kernel SYS and sample APO DLLs, without installation
- [x] Script and verify repeatable toolchain checks / compile-only build
- [ ] Develop Windows 10 compatible AuralWeave-owned virtual input/output drivers
- [ ] Validate complete INF/API/catalog and production signing
- [ ] Implement low-latency single-input/output audio transport to Native C++ core
- [ ] Test secure install, per-device removal, restart, and portable relocation

## Windows 10 / Windows 11 virtual driver prototype checkpoint

- [x] Adapt licensed Microsoft audio miniport sample for AuralWeave
- [x] Produce unsigned AuralWeaveVirtualAudio.sys kernel prototype
- [x] Separate Virtual Input/Virtual Output INF interface registrations
- [x] Select endpoint direction based on exact root PnP hardware ID
- [x] Reserve only one active ID per endpoint role
- [x] Target Windows 10 2004+ and Windows 11 x64 from shared INF
- [x] Pass WDK build, InfVerif /w /u and Inf2Cat signability checks
- [x] Guard manager discovery by class, exact ID and service
- [ ] Implement real low-latency per-instance audio bridge to Studio
- [ ] Verify per-instance stream isolation, PnP lifecycle and timing
- [ ] Test on Windows 10 22H2 and Windows 11 VMs
- [ ] Pass HLK and obtain production Microsoft driver signature
- [ ] Implement verified, optional administrator install/uninstall

## Virtual PCM user-mode transport checkpoint (2026-11-25)

- [x] Implement isolated Float32 Shared Memory SPSC sessions
- [x] Implement versioned mapping metadata and read/write frame cursors
- [x] Support user-mode virtual input/output roles and only slot 1
- [x] Implement bounded frame buffering, underrun silence and overrun counters
- [x] Test actual data transfer between two Windows processes
- [x] Export a native C ABI and add .NET bridge wrappers
- [x] Bind optional post-filter Studio tap to a Virtual Input shared channel
- [x] Test muted real WASAPI capture -> Gain -> 48-kHz PCM ring -> independent reader
- [ ] Design safe kernel/user ABI and per-device security authorization
- [ ] Bind WaveRT input/output miniport stream to each corresponding PCM ring
- [ ] Support the virtual speaker as a selectable source in Studio
- [ ] Validate clock drift, low latency, multi-device concurrency, PnP teardown
- [ ] Test on actual Windows 10/11 VMs, obtain valid production signature


## One Virtual Input / One Virtual Output (2026-11-25)

- The current INF exposes one PnP hardware ID for each role, both ending in 0001.
- The AuralWeave PnP manager allows future installation only when no current
  or legacy device of that role exists; unsigned driver installation stays disabled.
- Former 0002..0016 prototype IDs are recognized for safe, explicit cleanup.
- The developer-only Native Studio route now accepts a Virtual Output PCM source
  through the same ordered Filter graph and optional Virtual Input PCM tap.
- Windows 10 user-mode test: 57,600 frames consumed, 57,599 frames emitted,
  Gain applied, zero source overflow, physical output muted.
- Windows Kernel driver transport, signing and Windows 11 runtime validation
  have NOT yet been implemented or performed.


## Multi-Portable and kernel protocol checkpoint (2026-11-25)

- [x] Use one PnP identity per Virtual Input and Virtual Output machine-wide
- [x] Create role-specific cross-process Windows Global control leases
- [x] Guard managed virtual bridge creation and per-device removal
- [x] Test a second process from a different Portable folder
- [x] Verify independent Input/Output ownership and asynchronous release
- [x] Add 64-byte versioned PCM wire header and validated S16/F32 transfer
- [x] Compile protocol/ring source as BOTH user-mode test and WDK prototype
- [x] Test 64,000 PCM frames with 1,000 ring wrap cycles
- [ ] Implement secure IOCTL dispatch, owner authorization and WaveRT audio queues
- [ ] Test actual installed/approved kernel driver on Windows 10, then sign
- [ ] Keep Windows 11 as theoretical unless a real compatibility test is performed


## Install-only Virtual Driver UI and shared-client audio (2026-11-25)

- [x] Support multi-client user-mode audio mixing and independent fanout
- [x] Keep Windows device count at one Virtual Input and one Virtual Output
- [x] Stop holding a global role lock for the entire audio session
- [x] Track active audio sessions across Portable copies using Windows handles
- [x] Remove AuralWeave UI Uninstall button and privileged device-removal API
- [x] Detect current, legacy and partially bound PnP identities before install
- [x] Prepare package trust checks and administrator setup invocation
- [x] Disable INSTALL when the signed production package is missing
- [x] Document Windows Device Manager as the removal pathway
- [ ] Finish safe signed production setup helper and complete WaveRT transport
- [ ] Verify production installation/uninstallation on Windows 10 test machine
- [ ] Keep Windows 11 theoretical until runtime validation is available


## WaveRT internal PCM and installer-helper checkpoint (2026-11-25)

**Build-stage only. No Windows driver was installed or signed.**

The unsigned Windows 10 WDK prototype now compiles kernel-internal,
independently locked 64-KiB PCM rings for Virtual Input and Virtual
Output. The WaveRT microphone DMA path consumes queued PCM or emits
silence; WaveRT speaker DMA copies rendered frames into its bounded
PCM queue. The Microsoft Sample sine generator, file recording worker
and runtime registry toggles were unlinked. A global single-adapter
restriction was found and changed to allow **Input + Output together**
in the driver architecture. Digital rights that forbid copying disable
render PCM mirroring and flush queued frames.

No secure kernel device IOCTL or user broker is connected yet, so the
driver is NOT a usable installed Windows audio endpoint.

The separate Windows 10 DriverSetup project now builds as a self-contained
read-only preflight utility and contains future signed-only PnP SetupAPI
logic. The production certificate SHA-256 pin is intentionally blank,
so all actual installation requests remain disabled. Uninstallation
remains a Windows Device Manager operation.

See docs/WDK_WAVERT_PCM_CHECKPOINT.md for source, design and QA.


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


## Windows Portable trial pipeline checkpoint (2026-11-25)

- [x] Add non-invasive Windows Actions workflow for Studio-only x64 Portable ZIP
- [x] Gate artifact upload behind native build, PCM protocol mock, discovery and cross-folder user-mode tests
- [x] Exclude unsigned Kernel SYS/INF/CAT, developer setup and debug binaries from test distribution
- [x] Strengthen managed kernel response validation (role, operation, rate, channel geometry, transfer bounds)
- [x] Add deterministic negative protocol tests and Windows 10 user trial instructions
- [x] Confirm GitHub Actions run #22 completed successfully (Native/managed build, PCM mock, cross-folder and ZIP audit)
- [ ] Execute low-volume hardware listening test using an extracted ZIP on Windows 10 22H2
- [ ] Implement a tested, least-privilege broker with lifecycle, clock pacing and failure recovery
- [ ] Run actual signed Virtual Input and Virtual Output kernel-driver tests in a controlled Windows 10 VM
- [ ] Obtain required production driver signing and complete Windows 11 runtime verification

**Release gate:** CI artifact is an ordinary Portable Studio preview, not a
functional or installable Virtual Driver. Never publish the unsigned WDK
prototype or turn on the INSTALL button on the basis of compile/mock tests.

## Multi-client PCM kernel worker checkpoint (2026-11-25)

- [x] Add explicit source-level PCM worker with status preflight and single-use stop semantics
- [x] Add fail-stop exception publication and coalescing timer scheduling reference
- [x] Combine microphone sources and fan out one simulated kernel PCM channel to independent listeners
- [x] Add mock tests for stop, disconnected streams, device loss and multiple clients
- [x] Wire tests into Windows diagnostics and Portable preview workflow
- [x] Confirm Windows runner build #22 passed previous worker/coordinator tests
- [ ] Confirm Windows runner passes newer abrupt-owner-death recovery test
- [ ] Implement least-privilege broker IPC, per-client permissions and crash recovery
- [ ] Replace timer-based reference with event-driven audio clock and measured drift management
- [ ] Validate signed kernel transport in controlled Windows 10 environment

This is **not** a production broker, installed audio endpoint or CI pass.
The INSTALL action remains disabled and Windows 11 runtime is unverified.
See [coordinator checkpoint](KERNEL_PCM_COORDINATOR_CHECKPOINT.md).

## Portable client owner lifetime / v0.3.0 checkpoint (2026-11-25)

- [x] Add process-owned Windows event HANDLE to detect PCM creator death after hard kill
- [x] Preserve independent virtual PCM writers/readers and their SPSC mapping ABI
- [x] Auto-detach stale bus contributors/listeners without affecting surviving channels
- [x] Exercise real Windows 10 child-process kill and both Virtual Input / Output roles
- [x] Run native/managed Windows 10 diagnostics and create local Portable ZIP
- [ ] Confirm CI success on the final v0.3.0 release source and ZIP archive
- [ ] Test the extracted v0.3.0 Studio GUI with user listening hardware
- [ ] Add trusted service, authenticated per-user IPC and cancelable kernel I/O
- [ ] Test signed Kernel Virtual Driver on Windows 10, then Windows 11

Owner-lifetime events are recovery hints, **not** identity or ACL protection.
Studio-only release policy remains fail-closed for unsigned drivers.
See [owner crash recovery](PCM_OWNER_LIFETIME_RECOVERY.md).

## Separate user-mode mock Broker and same-user IPC (2026-11-25)

- [x] Create isolated non-elevated `AuralWeave.Broker` console host; explicit `--mock` only
- [x] Restrict mock IPC to the current Windows user with bounded local Named Pipe protocol
- [x] Add per-session singleton, role-specific GUID map admission and registration quotas
- [x] Serialize PCM pump/membership changes; mix independent mic rings and broadcast to listeners
- [x] Add managed `LocalBrokerControlClient` with serialized requests, heartbeat and fail-stop status
- [x] Test two PCM sources, surviving peers on QUIT/broken pipe, duplicate broker and oversized input
- [x] Integrate source build/diagnostics into Windows 10 and GitHub Actions
- [x] Verify separate portable Mock Broker ZIP after relocation on local Windows 10
- [ ] Verify portable Mock Broker ZIP relocation and tests in GitHub Actions
- [ ] Design/review privileged Windows service with SID ACLs and per-user permissions
- [ ] Replace mock loopback with validated signed kernel access and event-driven clock
- [ ] Verify Windows 10/11 installed virtual input/output endpoints, HLK and production signing

This is a development checkpoint; **not** a functional Windows virtual
audio driver or a replacement for the future privileged broker. Do not
enable INSTALL. Details: [Mock Broker](LOCAL_BROKER_MOCK.md).

## Mock Broker protocol v2 / explicit restart recovery (2026-11-25)

- [x] Add per-process broker lifetime GUID and mandatory versioned HELLO 2 handshake
- [x] Move breaking control wire protocol to separate Named Pipe v2
- [x] Add validated typed status, heartbeat counter, and dropped-listener telemetry
- [x] Add fresh mapping owner session helper for explicit fail-closed reconnect
- [x] Test actual hard kill and new process, with no replay of stale PCM
- [x] Test no unversioned access and reject fake same-instance reconnect
- [x] Test idle heartbeat with real Windows process and registered mappings
- [ ] Confirm GitHub Windows runner and relocated Mock Broker v0.2.0 ZIP checks
- [ ] Add real Studio-facing session UI and verified audio consent controls
- [ ] Implement privileged service security model and signed kernel transport

See [Mock Broker crash/restart checkpoint](BROKER_CRASH_RESTART_CHECKPOINT.md).

## Mock Broker v0.3.0 — advisory PCM process-origin binding

- [x] Introduce Native PCM mapping header v2 with advisory creator PID
- [x] Export owner PID from C++ C ABI and expose it through managed Native bridge
- [x] Read the actual connected pipe client process ID via Win32
- [x] Refuse source/listener mapping registration from mismatched process IDs
- [x] Test independent creator/claimant processes and correct cross-process PCM
- [x] Test failed admission leaves existing audio peers untouched
- [x] Test crashed owner is pruned without interrupting the other listeners
- [ ] Verify full v0.3.0 Mock Broker portable ZIP on GitHub Actions
- [ ] Design privileged broker authorization independent of mutable shared-memory data
- [ ] Implement signed kernel driver access and Windows 10/11 device tests

This is **advisory same-user mock admission only**, not authenticated or
privileged access control. Details: [PCM process-origin checkpoint](BROKER_PROCESS_ORIGIN_CHECKPOINT.md).

