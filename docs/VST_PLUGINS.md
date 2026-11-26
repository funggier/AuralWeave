# VST Plugin cards

AuralWeave can load installed 64-bit VST2 (.dll) and VST3 (.vst3) audio effects in the ordered Filters chain. Each card creates its own plugin instance and can be reordered or bypassed like an EQ or Compressor.

1. Click **Filters → +** and choose a VST card.
   - **VST Plugin (Custom Location)** starts with an empty path. Enter a folder and click **Scan**, or click **Browse…** to choose and scan it. The scan includes that folder and its subfolders.
   - **VST Plugin (Common Files)** scans the standard locations below when added. Click **Re-scan** after installing or updating plugins.
2. Pick a format from the results (for example, **Crystalline · VST2** or **Crystalline · VST3**) and click **Load Plugin**.
3. Click **Open editor** to adjust the effect. Click **Reload Plugin** to create a fresh instance of the selected effect, or select a different effect and click the same button to replace it. The previous effect keeps running if the replacement cannot load.
4. Start the audio route. Each enabled effect receives the input's sample rate. The processed signal goes to all configured Sound Output devices.

### Common Files scan locations

| Format | Windows locations |
| --- | --- |
| VST2 | `Program Files\Common Files\VST2`, `Program Files\Common Files\Steinberg\VST2`, `Program Files\VSTPlugins`, `Program Files\Steinberg\VSTPlugins` |
| VST3 | `Program Files\Common Files\VST3`, `%LOCALAPPDATA%\Programs\Common\VST3` |

[Steinberg Windows VST locations](https://helpcenter.steinberg.de/hc/en-us/articles/115000177084-VST-plug-in-locations-on-Windows) · [VST3 developer documentation](https://steinbergmedia.github.io/vst3_dev_portal/pages/Technical+Documentation/Locations+Format/Plugin+Locations.html)

Scans read executable headers and exports in the background. Add more cards to run multiple effects or instances.

Filter Presets remember the card's discovery source, the selected plugin file's location and identity, position, and on/off state. Paths under the portable folder are relative to the executable. Local plugin files elsewhere retain their full paths. A plugin file previously loaded in this copy of AuralWeave reopens automatically if its contents still match. For a new or replacement file, select the effect and click **Load Plugin**. Missing files restore as empty cards in the saved discovery mode.

Save effect parameters using the plugin's own preset controls before reloading an instance.

The x64 host loads VST2 effects with the `VSTPluginMain` export and VST3 standalone modules or Windows bundles with a `Contents/x86_64-win` module. VST3 uses the first audio class in each module and its mono or stereo main buses. MIDI instruments, AAX and 32-bit plugins require other hosts. Plugins run in the app process. AuralWeave supplies a fixed 120 BPM / 4/4 clock for effects that use host tempo. Licensing and activation of installed effects are managed by their publishers.

For Crystalline VST2, choose **Custom Location** and scan the folder used in its 64-bit installation. For Crystalline VST3 installed to the standard Windows folder, choose **Common Files**. [Crystalline manual](https://babyaud.io/docs/crystalline-manual)
