# VST Plugin cards

AuralWeave can load installed 64-bit VST2 audio-effect DLLs in the ordered Filters chain. Each card creates its own plugin instance and can be reordered or bypassed like an EQ or Compressor.

1. Click **Filters → +** and choose **VST Plugin**.
2. Enter the folder path or click **Browse…**. **Scan** lists matching DLLs in the folder and its subfolders. It reads the Windows executable headers and export names.
3. Select a plugin and click **Load plugin**. Add more cards to use multiple effects or multiple instances.
4. Click **Open editor** to adjust the effect in its own window. When the route starts, the loaded and enabled effects receive the selected input's sample rate. The processed signal goes to all configured Sound Output devices.

The last scanned folder is saved in **settings/vst-folder.txt** next to AuralWeave. A path inside the app folder is stored relative to the executable. Locations elsewhere on the machine retain their absolute paths. A portable copy can scan a new location after moving machines.

Filter Presets retain a VST card's position and on/off state. On restore the card starts empty so the DLL can be selected explicitly. Use the plugin's own preset controls for its effect parameters.

This host supports x64 VST2 DLLs with the VSTPluginMain export, using float audio input/output. Plugins run in the app process. VST3, AAX, 32-bit DLLs and instruments are outside this build's supported formats. AuralWeave supplies a fixed 120 BPM / 4/4 clock for effects that use host tempo. Licensing and activation of installed effects are managed by their publishers.

For Crystalline, select the folder used for its 64-bit VST2 installation. [Crystalline manual](https://babyaud.io/docs/crystalline-manual)
