# Third-party Components and Licensing

AuralWeave original source: MIT License, Copyright (c) 2026 funggier.
See the repository root `LICENSE`.

## Included in native v0.2.0

**RNNoise** — Xiph.Org, pinned upstream `v0.1.1`,
commit `6cbfd53eb348a8d394e0757b4025c6ded34eb2b6`.

- Purpose: optional neural noise reduction
- Distribution: source under `third_party/rnnoise`; embedded Standard
  weights from `src/rnn_data.c`, compiled into `AuralWeave.Native.dll`
- License: BSD 3-Clause. Full original notice: `third_party/rnnoise/COPYING`,
  copied in Windows ZIP as `licenses/RNNoise-v0.1.1-BSD-3-Clause.txt`
- Modifications: MSVC compatibility changes to two C files; see vendor README
- No RNNoise model downloads are performed when running or building.

**Windows Core Audio, .NET 8/WPF** — Microsoft platform/runtime components;
relevant redistribution conditions must be observed.

## Not included

NAudio was removed in earlier native-only versions.
Little/Custom RNNoise models, EQ, AI processing and virtual audio drivers have
not been bundled.

The project's MIT License does not relicense third-party software.

## Kernel driver prototype source (NOT bundled in the portable app)

**Microsoft Simple Audio Sample**, from `microsoft/Windows-driver-samples`,
repository commit `d49bb7d72f3651952b2b6e4ad28d14fe2247eb69`.

- Location: `src/AuralWeave.VirtualDriver/Kernel/Source/`
- License: **Microsoft Public License (MS-PL)**; full license text:
  `src/AuralWeave.VirtualDriver/Kernel/LICENSE.MS-PL.txt`
- Adaptations: separate input/output PnP models, AuralWeave-branded
  INF, per-hardware-ID role registration, prototype default mute of
  synthetic generator, renamed kernel output.
- This derived source remains **MS-PL** and does not acquire the root
  MIT project's license.
- Driver SYS/INF prototypes are **unsigned and not ready for
  installation or redistribution**. They are deliberately excluded
  from the Studio Portable ZIP and all public Releases.
