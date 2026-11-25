## Route audio between apps with VB-CABLE

For Windows virtual audio routing, use **VB-CABLE by VB-Audio**. Windows exposes `CABLE Input` as a playback device and `CABLE Output` as a recording device. Audio sent to the playback device comes out of the recording device. [Official download](https://vb-audio.com/Cable/)

Extract the VB-CABLE ZIP, run `VBCABLE_Setup_x64.exe` as administrator on Windows x64, and restart Windows. Open AuralWeave and click **Refresh devices** if it was already running after installation. [VB-CABLE installation manual](https://vb-audio.com/Cable/VBCABLE_ReferenceManual.pdf)

### Send processed audio to Discord or OBS

| Where | Device |
| --- | --- |
| AuralWeave Sound Input | Microphone or Line In |
| AuralWeave Sound Output | `CABLE Input` |
| Discord Voice & Video → Input Device | `CABLE Output` |
| Discord Voice & Video → Output Device | Your headphones |

Press **START** in AuralWeave and use Discord's **Mic Test** to check the signal. In OBS, select `CABLE Output` in an Audio Input Capture source. The destination receives the audio after AuralWeave applies its Filters. [Discord Mic Test](https://support.discord.com/hc/en-us/articles/360020641332-Mic-Testing)

### Receive audio from another app

Set that app's playback device to `CABLE Input`, select `CABLE Output` in AuralWeave **Sound Input**, and route to headphones or your chosen destination. For simultaneous send and receive on Discord, use two independent cables and a separate AuralWeave route in another Portable copy. Keep received Discord audio out of the cable feeding Discord's microphone to prevent echo. [VB-Audio additional cables](https://vb-audio.com/Cable/)
