using AuralWeave.Audio.Windows;
using AuralWeave.Core;

try
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    Console.WriteLine("AuralWeave device diagnostics");
    var sources = WasapiDeviceCatalog.CaptureDevices();
    var outputs = WasapiDeviceCatalog.RenderDevices();
    Console.WriteLine($"CAPTURE SOURCES: {sources.Count}");
    foreach (var source in sources)
        Console.WriteLine($"  [{source.Kind}] {source.DisplayName} | default={source.IsDefault} | id={source.Id}");
    Console.WriteLine($"RENDER OUTPUTS: {outputs.Count}");
    foreach (var output in outputs)
        Console.WriteLine($"  {output.Name} | default={output.IsDefault} | id={output.Id}");

    if (args.Contains("--formats"))
    {
        using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
        foreach (var device in sources.Where(x => x.Kind == AudioEndpointKind.Capture))
        {
            using var d = enumerator.GetDevice(device.Id.Substring("capture:".Length));
            var f = d.AudioClient.MixFormat;
            Console.WriteLine($"CAPTURE FORMAT [{d.FriendlyName}]: {f}");
        }
        foreach (var device in outputs)
        {
            using var d = enumerator.GetDevice(device.Id);
            var f = d.AudioClient.MixFormat;
            Console.WriteLine($"RENDER FORMAT [{d.FriendlyName}]: {f}");
        }
    }
    if (args.Contains("--smoke-native-multi"))
    {
        if (!NativeWasapiAudioRoute.Available)
            throw new InvalidOperationException("Native DLL missing from diagnostics output.");
        var input = sources.First(x => x.Kind == AudioEndpointKind.Capture);
        using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
        var specs = outputs.Select(endpoint =>
        {
            using var device = enumerator.GetDevice(endpoint.Id);
            return (endpoint, rate: device.AudioClient.MixFormat.SampleRate);
        }).ToArray();
        var first = specs.FirstOrDefault(x => x.rate == 192000);
        var second = specs.FirstOrDefault(x => x.rate == 48000);
        if (first.endpoint is null || second.endpoint is null)
            throw new InvalidOperationException("Need independent outputs at 192k and 48k for mixed-rate smoke.");
        Console.WriteLine($"NATIVE MULTI MUTED: {input.Name} -> {first.endpoint.Name} [{first.rate}Hz] + {second.endpoint.Name} [{second.rate}Hz]");
        using var route = new NativeWasapiAudioRoute();
        route.ReplaceEffects([new AudioEffectInstance(Guid.NewGuid(), AudioRouteRules.GainEffect, true, 0.8f)]);
        route.Start(input.Id, [new OutputRoute(Guid.NewGuid(), first.endpoint.Id, 1f, true),
                               new OutputRoute(Guid.NewGuid(), second.endpoint.Id, 1f, true)]);
        await Task.Delay(2500);
        var frames = route.CapturedFrames;
        var drops = route.DroppedRenderFrames;
        var error = route.LastError;
        route.Stop();
        Console.WriteLine($"Native mixed-rate frames: {frames}; ring overruns: {drops}; error: {error ?? "none"}");
        if (frames <= 0 || error != null)
            throw new InvalidOperationException("Native mixed-rate routing failed.");
        Console.WriteLine("PASS: Muted native multi-output routing across two sample rates.");
    }
    if (args.Contains("--smoke-native"))
    {
        if (!NativeWasapiAudioRoute.Available)
            throw new InvalidOperationException("Native DLL is not in the diagnostics output directory.");
        var input = sources.FirstOrDefault(x => x.Kind == AudioEndpointKind.Capture && x.IsDefault)
            ?? sources.FirstOrDefault(x => x.Kind == AudioEndpointKind.Capture)
            ?? throw new InvalidOperationException("No capture device.");
        var output = outputs.FirstOrDefault(x => x.IsDefault) ?? outputs.First();
        Console.WriteLine($"NATIVE SILENT: {input.Name} -> {output.Name} (muted)");
        using var route = new NativeWasapiAudioRoute();
        route.ReplaceEffects([new AudioEffectInstance(Guid.NewGuid(), AudioRouteRules.GainEffect, true, 0.5f),
            new AudioEffectInstance(Guid.NewGuid(), AudioRouteRules.GainEffect, true, 0.5f)]);
        route.Start(input.Id, [new OutputRoute(Guid.NewGuid(), output.Id, 1f, true)]);
        await Task.Delay(1500);
        var frames = route.CapturedFrames;
        var dropped = route.DroppedRenderFrames;
        var error = route.LastError;
        route.Stop();
        Console.WriteLine($"Native frames: {frames}, dropped: {dropped}, error: {error ?? "none"}");
        if (frames <= 0 || error != null)
            throw new InvalidOperationException("Native audio stream not healthy.");
        Console.WriteLine("PASS: Native muted capture -> render route and Stop.");
    }
    if (args.Contains("--smoke-silent"))
    {
        // Emits only silence to selected render endpoint. This validates device open,
        // capture callbacks, ring buffering and Stop() without audible output.
        var input = sources.FirstOrDefault(x => x.Kind == AudioEndpointKind.Capture && x.IsDefault)
            ?? sources.FirstOrDefault(x => x.Kind == AudioEndpointKind.Capture)
            ?? throw new InvalidOperationException("No capture input exists for silent smoke test.");
        var output = outputs.FirstOrDefault(x => x.IsDefault) ?? outputs.First();
        var selection = new OutputRoute(Guid.NewGuid(), output.Id, 1f, Muted: true);
        Console.WriteLine($"SILENT SMOKE: {input.Name} -> {output.Name} (Muted)");
        using var route = new WasapiAudioRoute();
        route.Start(input.Id, [selection]);
        await Task.Delay(1800);
        var frames = route.CapturedFrames;
        var errors = route.GetOutputTelemetry();
        var error = route.LastError ?? errors.Select(x => x.Error).FirstOrDefault(x => x is not null);
        route.Stop();
        Console.WriteLine($"Frames captured: {frames}; output telemetry: {errors.Count}; error: {error ?? "none"}");
        if (error is not null || frames == 0 || errors.Count != 1)
            throw new InvalidOperationException("Silent audio route smoke test failed.");
        Console.WriteLine("PASS: Muted real-device capture/render route and Stop.");
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine("Diagnostic failed: " + ex);
    return 1;
}
return 0;
