# VST Plugin cards

AuralWeave can load installed 64-bit VST2 (.dll) and VST3 (.vst3) audio effects in the ordered Filters chain. Each card creates its own plugin instance and can be reordered or bypassed like an EQ or Compressor.

1. Click **Filters → +** and choose **VST Plugin**.
2. Enter the folder path or click **Browse…**. **Scan** checks that folder and its subfolders plus the standard Windows VST3 directory. It reads executable headers and exports. The menu lists copies separately by format (for example, **Crystalline · VST2** and **Crystalline · VST3**).
3. Select a plugin and click **Load plugin**. Add more cards to use multiple effects or multiple instances.
4. Click **Open editor** to adjust the effect in its own window. When the route starts, the loaded and enabled effects receive the selected input's sample rate. The processed signal goes to all configured Sound Output devices.

New cards show **Choose VST plugins folder...** in an empty path field. Enter a folder or use **Browse** to scan it.

Filter Presets remember the selected plugin file's location, file identity, position and on/off state. Paths under the portable folder are relative to the executable. Local plugin files elsewhere retain their full paths. A plugin file previously loaded in this copy of AuralWeave reopens automatically if its contents still match. If a preset references a file new to this copy or a replacement file, use **Load plugin** on that card. Missing files restore as empty VST cards.

Use the plugin's own preset controls to save its effect parameters.

The x64 host loads VST2 effects with the `VSTPluginMain` export and VST3 standalone modules or Windows bundles with a `Contents/x86_64-win` module. VST3 uses the first audio class in each module and its mono or stereo main buses. MIDI instruments, AAX and 32-bit plugins require other hosts. Plugins run in the app process. AuralWeave supplies a fixed 120 BPM / 4/4 clock for effects that use host tempo. Licensing and activation of installed effects are managed by their publishers.

For Crystalline, scan the folder used for its 64-bit VST2 installation. The standard Windows VST3 directory is included in that search. [Crystalline manual](https://babyaud.io/docs/crystalline-manual)
