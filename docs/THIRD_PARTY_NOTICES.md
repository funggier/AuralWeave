# Third-party Components and Licensing

AuralWeave original source: MIT License, Copyright (c) 2026 funggier.
See the repository root `LICENSE`.

## Included in the Windows Portable package

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

## Optional external audio routing

VB-CABLE is distributed by VB-Audio under its own license and installed separately. The AuralWeave package links to the publisher and includes the VB-CABLE routing guide in `RELEASE_NOTES.md`.

The project's MIT License applies to original AuralWeave source.
