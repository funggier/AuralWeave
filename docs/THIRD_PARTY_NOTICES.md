# Third-party Components and Licensing

AuralWeave original project source: [MIT License](../LICENSE), Copyright (c) 2026 funggier.

## Current implementation

- Native C++ Windows Audio (WASAPI / MMDevice): uses Windows SDK interfaces and platform components. No third-party audio-processing library is bundled in this preview.
- Desktop interface: .NET 8 WPF; self-contained deployment redistributes applicable .NET runtime components subject to their respective Microsoft licenses and notices. Confirm full redistributable obligations before a public release.

## Planned, not bundled

| Candidate | Purpose | Current state |
| --- | --- | --- |
| RNNoise (Xiph) | Noise suppression | Not yet added; audit pinned upstream version/license/notice before bundling |
| Microsoft SysVAD sample | Research for optional virtual endpoints | Not bundled; source/driver redistribution and signing to be assessed |
| Future DSP and AI models | Additional voice effects | Not bundled; model rights must be verified separately |

## History

NAudio 2.2.1 was used only in an early managed-audio prototype. **It is no longer referenced by the project or included in native-only preview packaging.** Historical Git commits retain the earlier dependency and its license notice. This file describes the present product state.

Before distributing new third-party code: document its exact version, origin, rights, notices, and redistribution conditions. The project's MIT license does not relicense external software, models or drivers.
