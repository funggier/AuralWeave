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
