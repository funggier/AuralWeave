# AuralWeave — Windows x64 Portable Preview

## How to use Studio

1. Extract the app into a writable folder. Open `AuralWeave.exe` with `AuralWeave.Native.dll` beside it.
2. Select a microphone, Line In or playback loopback in **Sound Input**.
3. Add outputs with **+**. New outputs are unmuted, so start with headphones or low volume.
4. Configure the ordered **Filters** and press **START**. Use **STOP** before changing selected endpoints.
5. See `RELEASE_NOTES.md` for VB-CABLE setup and sending or receiving audio from other apps.

Gain, RNNoise Standard, Noise Gate, Limiter, Compressor, and 3-, 5- and 7-band EQ can be repeated, reordered and bypassed independently. Filter Presets can be saved, loaded, imported and exported. The skin selection is stored in the Portable folder.

## Known limitations


- WASAPI shared-mode mix formats are mono/stereo Float32 or PCM16. Unsupported formats produce an error.
- Long-run device drift, hotplug recovery and quantified end-to-end latency remain under test.
- Windows 10 22H2 x64 is the hardware test baseline. Windows 11 runtime verification is pending.
