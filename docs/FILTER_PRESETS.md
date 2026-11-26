# Filter Presets (Studio)

## Portable data

Preset storage: `presets/filters/*.awfilter` relative to the executable directory (the portable folder may be relocated). No user profile or registry dependency is added for this feature.

New files use `SchemaVersion=2` with `Name` and an ordered `Filters` array. Each entry stores `Kind`, `Enabled` and `Parameter` (semitones for Pitch Shift). EQ has an optional `Equalizer` object and Compressor an optional `Compressor` object. If the object is absent, the filter uses its defaults. Existing schema 1 presets remain readable.

**Never included:** Sound Input choice, audio endpoint IDs, Sound Output choices, output gains/mute, Windows hardware identification, or route session state.

## Controls

- **SAVE:** saves named local preset; prompts before overwrite.
- **LOAD:** selects an existing local preset and replaces the current filter chain only.
- **IMPORT:** choose an `.awfilter` file from any folder; validate; load it; then add it to local presets, prompting before overwrite.
- **EXPORT:** choose a destination for the current filters in `.awfilter` format.

Validation rejects unknown filter kinds, invalid parameter ranges, unsupported format version, duplicate slot IDs (internal), more than 64 slots, too-large files, or unexpected properties. The loading path builds the native graph before changing the visible Studio list, so rejected presets should not alter the active filter chain.

Settings and sound devices remain independent from all preset operations.

## Future enhancements

Preset rename/delete actions, optional grouping and preset descriptions can be implemented without expanding the preset format to include sound devices.
