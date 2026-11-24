# Third-party Components and Licensing

AuralWeave original project source is distributed under the [MIT License](../LICENSE), Copyright (c) 2026 funggier.

The MIT license **does not automatically relicense** third-party libraries, SDKs, drivers, models, model weights, or redistributed assets.

## Checklist before adding external code

- Record exact project, version, source URL, license and required attribution.
- Include upstream LICENSE/NOTICE text if required.
- Verify redistribution, modification, patent and commercial-use requirements before packaging.
- Keep model/data licenses separate from source-code licenses.
- Verify driver sample license, signing prerequisites and redistributable rights.
- Disclose optional noncommercial or restricted extensions and do not bundle them into an unrestricted release without explicit compatibility assessment.

## Planned candidates (not bundled)

| Candidate | Purpose | State |
| --- | --- | --- |
| RNNoise (Xiph) | Noise suppression | Planned; exact pinned version/license audit required |
| Microsoft WASAPI APIs | Windows capture/render | Planned; platform component |
| Microsoft SysVAD sample | Optional virtual device research | Reference only; not bundled |
| Future AI voice models | Optional DSP/voice effects | Undefined; licenses to be reviewed per model |

The table is an inventory of **intent**, not evidence that these dependencies are installed.
## Current prototype dependency (needs release audit)

| Component | Version | Integration | Status |
| --- | --- | --- | --- |
| NAudio | 2.2.1 | NuGet managed WASAPI bridge for prototype | NuGet license.txt verified as MIT (Copyright 2020 Mark Heath); local verbatim copy preserved in third_party/licenses/NAudio-2.2.1-LICENSE.txt |

The native graph currently uses Windows SDK headers/APIs and does not bundle a virtual audio driver or RNNoise model.

### NAudio 2.2.1 verified notice
Source: local NuGet package `naudio/2.2.1/license.txt` (MIT, Copyright 2020 Mark Heath). A verbatim copy is included under `third_party/licenses/` for packaging. Additional third-party dependencies and the .NET runtime may require separate license notices before public distribution.
