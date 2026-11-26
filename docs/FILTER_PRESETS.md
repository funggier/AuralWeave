# Filter Presets (Studio)

## Portable data

Preset storage: `presets/filters/*.awfilter` relative to the executable directory (the portable folder may be relocated). No user profile or registry dependency is added for this feature.

The files contain `Name` and an ordered `Filters` array. Presets with **EQ (5 Band)** or **EQ (7 Band)** use `SchemaVersion=3` and store optional `BandGainsDb` values from low to high frequency. Presets with the earlier filters continue to use schema 2. The `equalizer` entry and its `Equalizer` object still represent **EQ (3 Band)**. Schema 1 and 2 presets remain readable.

When loading or importing a v0.4.0 preset, the app skips the retired Pitch Shift stage and reports it in the status line. The other filters keep their settings and order. Loading a preset leaves its file on disk as it was.

**Never included:** Sound Input choice, audio endpoint IDs, Sound Output choices, output gains/mute, Windows hardware identification, or route session state.

Presets with a selected VST2 DLL use schema 5. They store the DLL path and its SHA-256 file identity. DLLs inside the portable folder use paths relative to the app. Outside DLLs use full paths. This portable copy automatically reopens matching DLLs it has previously loaded. If the file is missing, the VST card reopens empty. Schema 4 VST presets still load as empty VST cards. Plugin parameters are saved using the plugin's editor. [VST Plugin guide](VST_PLUGINS.md)

## Controls

- **SAVE:** saves named local preset; prompts before overwrite.
- **LOAD:** selects an existing local preset and replaces the current filter chain only.
- **IMPORT:** choose an `.awfilter` file from any folder; validate; load it; then add it to local presets, prompting before overwrite.
- **EXPORT:** choose a destination for the current filters in `.awfilter` format.

Validation rejects unknown filter kinds, invalid parameter ranges, unsupported format version, duplicate slot IDs (internal), more than 64 slots, too-large files, or unexpected properties. The loading path builds the native graph before changing the visible Studio list, so rejected presets should not alter the active filter chain.

Settings and sound devices remain independent from all preset operations.

## Future enhancements

Preset rename/delete actions, optional grouping and preset descriptions can be implemented without expanding the preset format to include sound devices.
