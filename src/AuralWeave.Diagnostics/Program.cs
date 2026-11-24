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
        Console.WriteLine($"  {output.Name} | default={output.IsDefault}" +
            $" | {output.SampleRate} Hz / {output.Channels} ch | id={output.Id}");

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
