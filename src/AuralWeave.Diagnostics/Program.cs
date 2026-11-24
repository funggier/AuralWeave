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
