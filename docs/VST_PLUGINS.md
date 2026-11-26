# VST Plugin cards

AuralWeave can load installed 64-bit VST2 audio-effect DLLs in the ordered Filters chain. Each card creates its own plugin instance and can be reordered or bypassed like an EQ or Compressor.

1. Click **Filters → +** and choose **VST Plugin**.
2. Enter the folder path or click **Browse…**. **Scan** lists matching DLLs in the folder and its subfolders. It reads the Windows executable headers and export names.
3. Select a plugin and click **Load plugin**. Add more cards to use multiple effects or multiple instances.
4. Click **Open editor** to adjust the effect in its own window. When the route starts, the loaded and enabled effects receive the selected input's sample rate. The processed signal goes to all configured Sound Output devices.

New cards show **Choose VST plugins folder...** in an empty path field. Enter a folder or use **Browse** to scan it.

Filter Presets remember the selected DLL's location, file identity, position and on/off state. Paths under the portable folder are relative to the executable. Local DLLs elsewhere retain their full paths. A DLL previously loaded in this copy of AuralWeave reopens automatically if its contents still match. If a preset references a DLL new to this copy or a replacement file, use **Load plugin** on that card. Missing files restore as empty VST cards.

Use the plugin's own preset controls to save its effect parameters.

This host supports x64 VST2 DLLs with the VSTPluginMain export, using float audio input/output. Plugins run in the app process. VST3, AAX, 32-bit DLLs and instruments are outside this build's supported formats. AuralWeave supplies a fixed 120 BPM / 4/4 clock for effects that use host tempo. Licensing and activation of installed effects are managed by their publishers.

For Crystalline, select the folder used for its 64-bit VST2 installation. [Crystalline manual](https://babyaud.io/docs/crystalline-manual)
