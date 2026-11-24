# Monochrome UI skins and icon assets

## Source artwork

The current program icon is the project-owner-selected **white–gray dimensional AN ribbon** with no tile, border, arrows, or equalizer bars. The surrounding canvas is genuinely alpha transparent.

Assets:
- `src/AuralWeave.Desktop/Assets/AuralWeave-Monochrome.png` — transparent monochrome application art, optimized for WPF resource packaging.
- `src/AuralWeave.Desktop/Assets/AuralWeave.ico` — generated multi-size Windows icon for EXE, window chrome, Taskbar and Explorer.
- `scripts/make-icon.ps1` — rebuilds the .ico using the PNG and Windows System.Drawing. Sizes: 16, 24, 32, 48, 64, 128 and 256px.

## Skins

Built-in palettes:
- `Monochrome Dark` — near-black background, charcoal cards, silver text, gray borders.
- `Graphite` — softer gray surfaces; same layout and typography.

The UI styles are in `src/AuralWeave.Desktop/App.xaml` and palette dictionaries are under `Themes/`. Visual components use `DynamicResource`, allowing palette switching without rebuilding the control tree and **without restarting Native WASAPI**.

The selected skin is persisted alongside the portable application at
`settings/appearance.json`. Nothing is intentionally written to the system drive by this feature. When installed in a read-only directory, switching still works for that session, but saving the preference may fail.

Skins are currently built into the app. **Untrusted external XAML skins are not loaded**, because WPF XAML can contain more than colors. Future user-installable `.awskin` packages should use a restricted data-only JSON schema for color and spacing tokens rather than arbitrary executable XAML.

## Filters UI scope

The current audio backend processes noise suppression (Bypass/RNNoise) before repeated Gain effects. Collapsing panels only changes visual state. Full modular reordering/repeating of arbitrary filters, including RNNoise, Noise Gate and Limiter, remains a separate native-DSP development milestone.

## Custom Chrome and themed scrollbar (preview.3)

- WPF WindowChrome renders a dark title bar and custom minimize/maximize/restore/close controls, while preserving the native resize border.
- Scrollbar track/thumb and gain/threshold slider styles share DynamicResource skin color tokens; no default bright controls.
- START is a light gray button; STOP is muted charcoal-gray. Control labels are uppercase.
- Filter UI starts with zero stages and adds Choose filter... instances. Processing now uses the native ordered filter graph; see [ORDERED_FILTER_CHAIN.md](ORDERED_FILTER_CHAIN.md).

## v0.2.0 AN ribbon artwork

The official AuralWeave icon was replaced with the project owner's selected
**white-to-gray AN satin ribbon**. It has dimensional soft silver shading
(not flat design), no surrounding dark tile, and a truly transparent external
background. The image is centered for Windows icon sizes (16/24/32/48/64/128/
256). To regenerate the image from the project-owner's source PNG, use
`python scripts/prepare-an-ribbon-icon.py source.png src/AuralWeave.Desktop/Assets/AuralWeave-Monochrome.png`
and then `scripts/make-icon.ps1`. Generated PNG/ICO artifacts are committed
so building does **not** depend on access to the source image in Downloads.