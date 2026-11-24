using AuralWeave.Audio.Windows;
using AuralWeave.Core;

try
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    Console.WriteLine("AuralWeave diagnostics — Native C++ WASAPI only");

    var sources = NativeAudioDeviceCatalog.CaptureDevices();
    var outputs = NativeAudioDeviceCatalog.RenderDevices();
    Console.WriteLine($"CAPTURE / LOOPBACK SOURCES: {sources.Count}");
    foreach (var source in sources)
        Console.WriteLine($"  [{source.Kind}] {source.DisplayName} | default={source.IsDefault}" +
            $" | {source.SampleRate} Hz / {source.Channels} ch | id={source.Id}");
    Console.WriteLine($"RENDER OUTPUTS: {outputs.Count}");
    foreach (var output in outputs)
        Console.WriteLine($"  {output.DisplayName} | default={output.IsDefault}" +
            $" | {output.SampleRate} Hz / {output.Channels} ch | id={output.Id}");

    if (args.Contains("--test-virtual-driver-discovery"))
    {
        if (!AuralWeaveVirtualDrivers.TryIdentifyRole(
                @"ROOT\AURALWEAVE_VINPUT_0001", out var inputRole) ||
            inputRole != VirtualAudioEndpointRole.Input ||
            !AuralWeaveVirtualDrivers.TryIdentifyRole(
                @"ROOT\AURALWEAVE_VOUTPUT_0001", out var outputRole) ||
            outputRole != VirtualAudioEndpointRole.Output ||
            AuralWeaveVirtualDrivers.TryIdentifyRole(
                @"USB\VID_1234&PID_5678", out _) ||
            AuralWeaveVirtualDrivers.TryIdentifyRole(
                @"ROOT\AURALWEAVE_VINPUTOTHER", out _) ||
            AuralWeaveVirtualDrivers.TryIdentifyRole(
                @"ROOT\AURALWEAVE_VINPUT", out _) ||
            AuralWeaveVirtualDrivers.TryIdentifyRole(
                @"ROOT\AURALWEAVE_VINPUT_0000", out _) ||
            AuralWeaveVirtualDrivers.TryIdentifyRole(
                @"ROOT\AURALWEAVE_VOUTPUT_0017", out _) ||
            AuralWeaveVirtualDrivers.TryIdentifyRole(
                @"ROOT\AURALWEAVE_VOUTPUT_0001&EXTRA", out _) ||
            !AuralWeaveVirtualDrivers.TryIdentifyRole(
                @"root\auralweave_vinput_0001", out var inputOne) ||
            inputOne != VirtualAudioEndpointRole.Input ||
            AuralWeaveVirtualDrivers.TryIdentifyRole(
                @"ROOT\AURALWEAVE_VINPUT_0002", out _) ||
            AuralWeaveVirtualDrivers.TryIdentifyRole(
                @"ROOT\AURALWEAVE_VOUTPUT_0016", out _) ||
            !AuralWeaveVirtualDrivers.TryIdentifyLegacyRole(
                @"ROOT\AURALWEAVE_VINPUT_0002", out var legacyInput) ||
            legacyInput != VirtualAudioEndpointRole.Input ||
            !AuralWeaveVirtualDrivers.TryIdentifyLegacyRole(
                @"ROOT\AURALWEAVE_VOUTPUT_0016", out var legacyOutput) ||
            legacyOutput != VirtualAudioEndpointRole.Output)
            throw new InvalidOperationException(
                "Virtual Driver ownership matching is incorrect.");
        if (AuralWeaveVirtualDrivers.MaxVirtualInputs != 1 ||
            AuralWeaveVirtualDrivers.MaxVirtualOutputs != 1 ||
            !AuralWeaveVirtualDrivers.MayInstall([], VirtualAudioEndpointRole.Input) ||
            !AuralWeaveVirtualDrivers.MayInstall([], VirtualAudioEndpointRole.Output))
            throw new InvalidOperationException("Single-endpoint capacity contract is invalid.");
        var simulatedInstalled = new[] {
            new AuralWeaveVirtualDevice(@"ROOT\AURALWEAVE_VINPUT_0001", "VInput",
                VirtualAudioEndpointRole.Input, AuralWeaveVirtualDrivers.InputHardwareId),
            new AuralWeaveVirtualDevice(@"ROOT\AURALWEAVE_VOUTPUT_0001", "VOutput",
                VirtualAudioEndpointRole.Output, AuralWeaveVirtualDrivers.OutputHardwareId)
        };
        if (AuralWeaveVirtualDrivers.MayInstall(simulatedInstalled, VirtualAudioEndpointRole.Input) ||
            AuralWeaveVirtualDrivers.MayInstall(simulatedInstalled, VirtualAudioEndpointRole.Output))
            throw new InvalidOperationException("Duplicate virtual driver installation must be blocked.");
        var legacyInstalled = new[] {
            new AuralWeaveVirtualDevice(@"ROOT\AURALWEAVE_VINPUT_0002", "Old VInput",
                VirtualAudioEndpointRole.Input, @"ROOT\AURALWEAVE_VINPUT_0002")
        };
        if (!legacyInstalled[0].IsLegacy ||
            AuralWeaveVirtualDrivers.MayInstall(legacyInstalled, VirtualAudioEndpointRole.Input))
            throw new InvalidOperationException("Legacy device must be discoverable and block installation.");
        Console.WriteLine("PASS: one current PnP ID per role, duplicate install guard and legacy cleanup recognition.");
        var installed = AuralWeaveVirtualDrivers.EnumerateInstalled();
        Console.WriteLine($"PASS: Windows SetupAPI discovery; {installed.Count} owned devices found.");
        foreach (var device in installed)
            Console.WriteLine($"  [{device.Role}] {device.Name} ({device.InstanceId})");
        Console.WriteLine("PASS: unrelated PnP devices never match AuralWeave namespace.");
    }
    if (args.Contains("--smoke-virtual-bridge"))
    {
        // Safe real-WASAPI capture test: muted physical output, optional
        // user-mode shared PCM tap. NEVER installs a Windows driver.
        var input = sources.FirstOrDefault(x => x.Kind == AudioEndpointKind.Capture && x.IsDefault)
            ?? sources.FirstOrDefault(x => x.Kind == AudioEndpointKind.Capture)
            ?? throw new InvalidOperationException("No usable capture endpoint.");
        var output = outputs.FirstOrDefault(x => x.IsDefault)
            ?? outputs.FirstOrDefault()
            ?? throw new InvalidOperationException("No usable render endpoint.");
        Console.WriteLine("VIRTUAL PCM TAP: native physical capture -> filters -> user-mode bridge; output muted.");
        using var route = new NativeWasapiAudioRoute();
        using var owner = NativeVirtualAudioBridge.Create(1, 1, 48000, 2, 4096);
        using var peer = NativeVirtualAudioBridge.Attach(owner.Name, 1, 1);
        route.ReplaceFilterChain([
            new FilterStage(Guid.NewGuid(), FilterKind.Gain, true, 0.75f)
        ]);
        route.Start(input.Id, [new OutputRoute(Guid.NewGuid(), output.Id, 1f, true)]);
        try
        {
            route.BindVirtualInputBridge(owner);
            // Consume continuously; avoid intentionally saturating the
            // bounded ring before beginning playback.
            var buffer = new float[1920]; // 960 stereo frames per read
            ulong framesReceived = 0;
            var end = DateTime.UtcNow.AddMilliseconds(1200);
            while (DateTime.UtcNow < end)
            {
                framesReceived += peer.Read(buffer);
                await Task.Delay(5);
            }
            var stats = owner.GetStats();
            var captures = route.CapturedFrames;
            Console.WriteLine($"Capture frames: {captures}; bridge wrote: {stats.FramesWritten}; " +
                $"bridge read: {framesReceived}; dropped: {stats.OverflowFrames}; " +
                $"underruns: {stats.UnderflowFrames}; rate: {stats.SampleRate}; " +
                $"channels: {stats.Channels}");
            if (captures <= 0 || stats.FramesWritten == 0 || framesReceived == 0 ||
                stats.SampleRate != 48000 || stats.Channels != 2 || route.LastError is not null)
                throw new InvalidOperationException("Post-filter Virtual Input PCM tap did not stream.");
            Console.WriteLine("PASS: WASAPI capture -> active native Filter Chain -> shared PCM tap -> independent consumer.");
            route.UnbindVirtualInputBridge();
        }
        finally
        {
            route.Stop();
        }
    }

    if (args.Contains("--smoke-virtual-output"))
    {
        // This creates TWO user-mode Shared Memory mappings and opens only
        // a muted physical playback device. No kernel driver is installed.
        var output = outputs.FirstOrDefault(x => x.IsDefault)
            ?? outputs.FirstOrDefault()
            ?? throw new InvalidOperationException("No physical render endpoint available.");
        Console.WriteLine("VIRTUAL OUTPUT ROUTE TEST: simulated output PCM -> Studio Gain Filter -> virtual input PCM (muted physical output).");
        using var virtualOutput = NativeVirtualAudioBridge.Create(2, 1, 48000, 2, 4096);
        using var virtualInput = NativeVirtualAudioBridge.Create(1, 1, 48000, 2, 4096);
        using var virtualMicReader = NativeVirtualAudioBridge.Attach(virtualInput.Name, 1, 1);
        using var route = new NativeWasapiAudioRoute();
        route.ReplaceFilterChain([
            new FilterStage(Guid.NewGuid(), FilterKind.Gain, true, 0.5f)
        ]);
        route.Start("virtual-output:" + virtualOutput.Name,
            [new OutputRoute(Guid.NewGuid(), output.Id, 1f, true)]);
        try
        {
            route.BindVirtualInputBridge(virtualInput);
            var inputPcm = Enumerable.Repeat(0.2f, 480 * 2).ToArray();
            var readerPcm = new float[960 * 2];
            ulong wrote = 0, received = 0, exactGainSamples = 0;
            for (var i = 0; i < 120; i++)
            {
                wrote += virtualOutput.Write(inputPcm);
                await Task.Delay(10);
                var frames = virtualMicReader.Read(readerPcm);
                received += frames;
                for (var j = 0; j < (int)frames * 2; j++)
                {
                    if (Math.Abs(readerPcm[j] - 0.1f) < 0.0001f)
                        exactGainSamples++;
                }
            }
            var fromVirtual = route.CapturedFrames;
            var statsIn = virtualInput.GetStats();
            var statsOut = virtualOutput.GetStats();
            Console.WriteLine($"Virtual Output source wrote {wrote}, Native Studio received {fromVirtual}, " +
                $"Virtual Input wrote {statsIn.FramesWritten}, read {received}, " +
                $"Gain-processed samples {exactGainSamples}, Output dropped {statsOut.OverflowFrames}");
            if (wrote < 24000 || fromVirtual < 24000 || received < 10000 ||
                exactGainSamples < 10000 || route.LastError is not null)
                throw new InvalidOperationException("Virtual Output source -> native filters -> Virtual Input tap failed.");
            Console.WriteLine("PASS: Virtual Output (user mode) -> native Studio filters -> Virtual Input (user mode).");
            // A closed source must terminate the Native route, even if no
            // further PCM arrives. Stop must still be idempotent afterward.
            virtualOutput.Dispose();
            await Task.Delay(100);
            if (route.IsRunning)
                throw new InvalidOperationException("Native route continued running after virtual source closed.");
            Console.WriteLine("PASS: Virtual Output producer shutdown stops Native route cleanly.");
        }
        finally
        {
            route.UnbindVirtualInputBridge();
            route.Stop();
        }
    }

    var single = args.Contains("--smoke-native") || args.Contains("--smoke-silent");
    var mixed = args.Contains("--smoke-native-multi") ||
                args.Contains("--soak-native-multi") ||
                args.Contains("--long-soak-native-multi");
    if (single)
    {
        var input = sources.FirstOrDefault(x => x.Kind == AudioEndpointKind.Capture && x.IsDefault)
            ?? sources.FirstOrDefault(x => x.Kind == AudioEndpointKind.Capture)
            ?? throw new InvalidOperationException("No active capture input.");
        var output = outputs.FirstOrDefault(x => x.IsDefault)
            ?? outputs.FirstOrDefault()
            ?? throw new InvalidOperationException("No active render output.");

        Console.WriteLine($"NATIVE SILENT: {input.Name} -> {output.Name} (muted)");
        using var route = new NativeWasapiAudioRoute();
        route.ReplaceEffects([
            new AudioEffectInstance(Guid.NewGuid(), AudioRouteRules.GainEffect, true, 0.5f),
            new AudioEffectInstance(Guid.NewGuid(), AudioRouteRules.GainEffect, true, 0.5f)
        ]);
        route.Start(input.Id, [new OutputRoute(Guid.NewGuid(), output.Id, 1f, true)]);
        await Task.Delay(1500);
        var frames = route.CapturedFrames;
        var overrun = route.DroppedRenderFrames;
        var underrun = route.UnderrunRenderFrames;
        var error = route.LastError;
        route.Stop();
        Console.WriteLine($"Native frames: {frames}; overrun: {overrun}; underrun: {underrun}; error: {error ?? "none"}");
        if (frames <= 0 || error is not null)
            throw new InvalidOperationException("Native single-output muted smoke failed.");
        Console.WriteLine("PASS: Native muted capture -> render route and Stop.");
    }

    if (args.Contains("--smoke-rnnoise-toggle") || args.Contains("--soak-rnnoise"))
    {
        var input = sources.FirstOrDefault(x => x.Kind == AudioEndpointKind.Capture && x.SampleRate == 192000)
            ?? sources.FirstOrDefault(x => x.Kind == AudioEndpointKind.Capture)
            ?? throw new InvalidOperationException("No physical capture device available.");
        var output1 = outputs.FirstOrDefault(x => x.IsDefault)
            ?? outputs.FirstOrDefault()
            ?? throw new InvalidOperationException("No render endpoints.");
        var output2 = outputs.FirstOrDefault(x => x.Id != output1.Id && x.SampleRate == 48000)
            ?? outputs.FirstOrDefault(x => x.Id != output1.Id);
        var selected = new List<OutputRoute> { new(Guid.NewGuid(), output1.Id, 1f, true) };
        if (output2 is not null)
            selected.Add(new OutputRoute(Guid.NewGuid(), output2.Id, 1f, true));
        Console.WriteLine($"RNNOISE TOGGLE (muted): {input.Name}, {selected.Count} output(s)");
        using var route = new NativeWasapiAudioRoute();
        route.SetNoiseMode(NoiseProcessingMode.Bypass);
        route.Start(input.Id, selected);
        await Task.Delay(1000);
        var before = route.CapturedFrames;
        route.SetNoiseMode(NoiseProcessingMode.RnnoiseStandard);
        await Task.Delay(args.Contains("--soak-rnnoise") ? 10000 : 1800);
        var during = route.CapturedFrames;
        var underrunsAtNoiseEnd = route.UnderrunRenderFrames;
        route.SetNoiseMode(NoiseProcessingMode.Bypass);
        await Task.Delay(800);
        var after = route.CapturedFrames;
        var errors = route.LastError;
        var underruns = route.UnderrunRenderFrames;
        var overruns = route.DroppedRenderFrames;
        route.Stop();
        Console.WriteLine($"Bypass frames: {before}; RNNoise frames: {during-before}; " +
            $"resumed bypass frames: {after-during}; overruns: {overruns}; " +
            $"underruns: {underruns}; after noise: {underruns-underrunsAtNoiseEnd}; " +
            $"error: {errors ?? "none"}");
        if (!(before > 0 && during > before && after > during && errors is null))
            throw new InvalidOperationException("RNNoise live switch diagnostic failed.");
        Console.WriteLine("PASS: Native RNNoise ↔ Bypass live switching, muted endpoints and Stop.");
    }
    if (args.Contains("--smoke-filter-chain"))
    {
        var input = sources.FirstOrDefault(x => x.Kind == AudioEndpointKind.Capture && x.IsDefault)
            ?? sources.First(x => x.Kind == AudioEndpointKind.Capture);
        var out1 = outputs.FirstOrDefault(x => x.IsDefault) ?? outputs.First();
        var out2 = outputs.FirstOrDefault(x => x.Id != out1.Id && x.SampleRate != out1.SampleRate);
        var sinks = new List<OutputRoute> { new(Guid.NewGuid(), out1.Id, 1f, true) };
        if (out2 is not null)
            sinks.Add(new OutputRoute(Guid.NewGuid(), out2.Id, 1f, true));
        Console.WriteLine($"ORDERED FILTERS (muted): {input.DisplayName} → {sinks.Count} outputs");
        using var route = new NativeWasapiAudioRoute();
        route.ReplaceFilterChain([]); // zero selected filters is true bypass
        route.Start(input.Id, sinks);
        await Task.Delay(600);
        var noFilters = route.CapturedFrames;
        route.ReplaceFilterChain([
            new FilterStage(Guid.NewGuid(), FilterKind.Gain, true, .8f),
            new FilterStage(Guid.NewGuid(), FilterKind.NoiseGate, true, -45f),
            new FilterStage(Guid.NewGuid(), FilterKind.RnnoiseStandard, true, 0f),
            new FilterStage(Guid.NewGuid(), FilterKind.Limiter, true, -1f),
            new FilterStage(Guid.NewGuid(), FilterKind.RnnoiseStandard, false, 0f)
        ]);
        await Task.Delay(2400);
        var processed = route.CapturedFrames;
        var errorDuring = route.LastError;
        route.ReplaceFilterChain([
            new FilterStage(Guid.NewGuid(), FilterKind.Limiter, true, -2f),
            new FilterStage(Guid.NewGuid(), FilterKind.RnnoiseStandard, true, 0f),
            new FilterStage(Guid.NewGuid(), FilterKind.Gain, true, .9f)
        ]);
        await Task.Delay(700);
        var reordered = route.CapturedFrames;
        route.ReplaceFilterChain([]); // back to unprocessed signal
        await Task.Delay(600);
        var resumed = route.CapturedFrames;
        var error = route.LastError;
        var overruns = route.DroppedRenderFrames;
        var underruns = route.UnderrunRenderFrames;
        route.Stop();
        Console.WriteLine($"Frames: {noFilters} → {processed} → {reordered} → {resumed}; " +
            $"overruns={overruns}; underruns={underruns}; error={error ?? errorDuring ?? "none"}");
        if (!(noFilters > 0 && processed > noFilters && reordered > processed &&
              resumed > reordered && error is null && errorDuring is null))
            throw new InvalidOperationException("Ordered native filter chain smoke failed.");
        Console.WriteLine("PASS: Native ordered filter chain / reorder / bypass / Stop.");
    }
    if (mixed)
    {
        var input = sources.FirstOrDefault(x => x.Kind == AudioEndpointKind.Capture)
            ?? throw new InvalidOperationException("No active capture input.");
        var first = outputs.FirstOrDefault(x => x.SampleRate == 192000);
        var second = outputs.FirstOrDefault(x => x.SampleRate == 48000);
        if (first is null || second is null)
            throw new InvalidOperationException("Mixed-rate smoke requires 192k and 48k outputs.");
        Console.WriteLine($"NATIVE MULTI MUTED: {input.Name} -> {first.Name} [192k] + {second.Name} [48k]");
        using var route = new NativeWasapiAudioRoute();
        route.ReplaceEffects([
            new AudioEffectInstance(Guid.NewGuid(), AudioRouteRules.GainEffect, true, 0.8f)
        ]);
        route.Start(input.Id, [
            new OutputRoute(Guid.NewGuid(), first.Id, 1f, true),
            new OutputRoute(Guid.NewGuid(), second.Id, 1f, true)
        ]);
        await Task.Delay(2000);
        var warmupOverrun = route.DroppedRenderFrames;
        var warmupUnderrun = route.UnderrunRenderFrames;
        var duration = args.Contains("--long-soak-native-multi") ? 28000
            : args.Contains("--soak-native-multi") ? 8000 : 500;
        await Task.Delay(duration);
        var frames = route.CapturedFrames;
        var overruns = route.DroppedRenderFrames;
        var underruns = route.UnderrunRenderFrames;
        var error = route.LastError;
        route.Stop();
        Console.WriteLine($"Frames: {frames}; overruns: {overruns}; underruns: {underruns};" +
            $" post-warmup overrun delta: {overruns - warmupOverrun};" +
            $" post-warmup underrun delta: {underruns - warmupUnderrun};" +
            $" error: {error ?? "none"}");
        if (frames <= 0 || error is not null)
            throw new InvalidOperationException("Native mixed-rate muted smoke failed.");
        Console.WriteLine("PASS: Native muted mixed-rate multi-output and Stop.");
    }
}
catch (Exception error)
{
    Console.Error.WriteLine("Diagnostic failed: " + error);
    return 1;
}
return 0;
