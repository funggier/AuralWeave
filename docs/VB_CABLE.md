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

### Reduce monitoring delay

If you need to hear your processed voice while using Discord, add your headphones as a second **Sound Output** in Studio. This sends the same processed audio directly to the headphones. Discord's Mic Test plays the received signal back through its selected output and is useful for checking that the signal arrives. [Discord Mic Test](https://support.discord.com/hc/en-us/articles/360020641332-Mic-Testing)

For a voice route, try matching the cable's formats at **48,000 Hz**. Stop the route and close the apps using the cable. Open `mmsys.cpl`, select `CABLE Input` under **Playback** and `CABLE Output` under **Recording**, and set each device's **Properties → Advanced → Default Format** to an available 48,000 Hz format. In **VB-CABLE Control Panel → Options**, check that **Internal Sampling Rate** is also 48,000 Hz. Matching all three rates avoids extra conversion inside the cable. Reopen the apps after changing the formats. [VB-CABLE Reference Manual, pp. 11–12](https://vb-audio.com/Cable/VBCABLE_ReferenceManual.pdf#page=11)

If you still hear delay, use **Latency → Set Current Latency** in the control panel while running the apps that send and receive. Check the input and output buffer statistics before choosing a lower value. If audio cuts out, the latency indicator turns red, or loss counters climb steadily, return to the prior setting. [VB-CABLE Reference Manual, pp. 12, 14–15](https://vb-audio.com/Cable/VBCABLE_ReferenceManual.pdf#page=14)
