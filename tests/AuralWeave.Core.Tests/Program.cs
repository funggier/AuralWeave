using System.Buffers.Binary;
using AuralWeave.Core;

var count = 0;
void Verify(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    Console.WriteLine("PASS: " + name);
    count++;
}

var endpoints = Enumerable.Range(0, 3).Select(i =>
    new AudioEndpoint($"device:{i}", $"Speaker {i}", AudioEndpointKind.Render)).ToArray();
var selected = new List<OutputRoute>();
Verify(AudioRouteRules.AvailableOutputs(endpoints, selected).Count == 3, "all outputs initially selectable");
selected.Add(new OutputRoute(Guid.NewGuid(), endpoints[0].Id));
Verify(AudioRouteRules.AvailableOutputs(endpoints, selected).Count == 2, "selected output is excluded");
selected.Add(new OutputRoute(Guid.NewGuid(), endpoints[1].Id));
selected.Add(new OutputRoute(Guid.NewGuid(), endpoints[2].Id));
Verify(AudioRouteRules.AvailableOutputs(endpoints, selected).Count == 0, "all selected disables add");
selected.RemoveAt(1);
Verify(AudioRouteRules.AvailableOutputs(endpoints, selected).Single().Id == endpoints[1].Id,
    "removed output becomes selectable again");
try
{
    AudioRouteRules.ValidateOutputs([
        new OutputRoute(Guid.NewGuid(), endpoints[0].Id),
        new OutputRoute(Guid.NewGuid(), endpoints[0].Id)]);
    throw new Exception("duplicate endpoints incorrectly accepted");
}
catch (ArgumentException ex) when (ex.Message.Contains("twice"))
{
    Verify(true, "duplicate outputs rejected");
}

var effects = new AudioBlockProcessor();
var eq1 = new AudioEffectInstance(Guid.NewGuid(), AudioRouteRules.GainEffect, true, 0.5f);
var eq2 = new AudioEffectInstance(Guid.NewGuid(), AudioRouteRules.GainEffect, true, 0.5f);
effects.ReplaceEffects([eq1, eq2]);
Verify(effects.Effects.Count == 2 && effects.Effects[0].Id != effects.Effects[1].Id &&
       effects.Effects[0].TypeId == effects.Effects[1].TypeId,
       "repeated effect types preserve independent instance IDs");

var buffer = new byte[4];
BinaryPrimitives.WriteInt32LittleEndian(buffer, BitConverter.SingleToInt32Bits(1f));
effects.Process(buffer, PcmSampleEncoding.Float32);
Verify(Math.Abs(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer)) - .25f) < 1e-6,
    "repeated effects process in order");
effects.ReplaceEffects([eq1, eq2 with { Enabled = false }]);
BinaryPrimitives.WriteInt32LittleEndian(buffer, BitConverter.SingleToInt32Bits(1f));
effects.Process(buffer, PcmSampleEncoding.Float32);
Verify(Math.Abs(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer)) - .5f) < 1e-6,
    "per-instance bypass preserves other effect");
effects.ReplaceEffects([]);
BinaryPrimitives.WriteInt32LittleEndian(buffer, BitConverter.SingleToInt32Bits(.9f));
effects.Process(buffer, PcmSampleEncoding.Float32);
Verify(Math.Abs(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer)) - .9f) < 1e-6,
    "empty effect chain bypasses cleanly");
effects.ReplaceEffects([new AudioEffectInstance(Guid.NewGuid(), AudioRouteRules.GainEffect, true, 2f)]);
BinaryPrimitives.WriteInt32LittleEndian(buffer, BitConverter.SingleToInt32Bits(.9f));
effects.Process(buffer, PcmSampleEncoding.Float32);
Verify(Math.Abs(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer)) - 1f) < 1e-6,
    "float gain saturates without overflow");
var pcm16 = new byte[2];
BinaryPrimitives.WriteInt16LittleEndian(pcm16, 20000);
effects.Process(pcm16, PcmSampleEncoding.Int16);
Verify(BinaryPrimitives.ReadInt16LittleEndian(pcm16) == 32767, "pcm16 gain saturates");
Verify(Math.Abs(AudioBlockProcessor.Peak(pcm16, PcmSampleEncoding.Int16) - (32767f / 32768f)) < .0001f,
    "pcm16 peak meter reads true sample");
try
{
    effects.ReplaceEffects([eq1, eq1]);
    throw new Exception("duplicate instance IDs incorrectly accepted");
}
catch (ArgumentException ex) when (ex.Message.Contains("unique"))
{
    Verify(true, "duplicate instance ID rejected");
}

var lineIn = new AudioEndpoint("capture:{id}", "Line In (Realtek(R) Audio)",
    AudioEndpointKind.Capture, true, 192000, 2);
Verify(lineIn.DisplayName.StartsWith(lineIn.Name + " {", StringComparison.Ordinal),
    "endpoint display starts with the exact Windows Name");
Verify(lineIn.DisplayName.Contains("192 kHz") &&
       lineIn.DisplayName.Contains("Stereo") &&
       !lineIn.DisplayName.Contains(lineIn.Id),
    "additional format metadata follows Name without leaking ID");
var playback = lineIn with { Kind = AudioEndpointKind.Loopback, Name = "Speakers (Realtek(R) Audio)" };
Verify(playback.DisplayName.StartsWith(playback.Name + " {") &&
       playback.DisplayName.Contains("Loopback"),
    "playback loopback uses friendly name then metadata in braces");
Verify(lineIn.ToString() == lineIn.DisplayName,
    "closed ComboBox selection renders friendly label rather than record dump");
var strangeRate = lineIn with { SampleRate = 44100, Channels = 1 };
Verify(strangeRate.DisplayName.Contains("44.1 kHz · Mono"),
    "noninteger kHz and mono label formatted correctly");

var slots = new[]
{
    new FilterStage(Guid.NewGuid()),
    new FilterStage(Guid.NewGuid(), FilterKind.RnnoiseStandard, true, 0f),
    new FilterStage(Guid.NewGuid(), FilterKind.RnnoiseStandard, true, 0f),
    new FilterStage(Guid.NewGuid(), FilterKind.NoiseGate, true, -45f),
    new FilterStage(Guid.NewGuid(), FilterKind.Limiter, true, -1f),
    new FilterStage(Guid.NewGuid(), FilterKind.Gain, true, 0.5f)
};
FilterStageRules.Validate(slots);
Verify(new FilterStage(Guid.NewGuid()).Enabled,
    "new filter slots are enabled (unmuted) by default");
Verify(!new OutputRoute(Guid.NewGuid(), "speaker:example").Muted,
    "new output route model defaults to unmuted");
Verify(slots[0].Kind == FilterKind.None &&
       slots.Count(x => x.Kind == FilterKind.RnnoiseStandard) == 2,
    "blank slot and repeated RNNoise filters are accepted");
Verify(FilterStage.DefaultParameter(FilterKind.Gain) == 1f &&
       FilterStage.DefaultParameter(FilterKind.Limiter) == -1f,
    "new filter parameters have safe defaults");
try
{
    FilterStageRules.Validate(new[] { slots[1], slots[1] });
    throw new Exception("duplicate filter IDs should fail");
}
catch (ArgumentException)
{
    Verify(true, "duplicate filter instance IDs are rejected");
}
try
{
    FilterStageRules.Validate(new[] {
        new FilterStage(Guid.NewGuid(), FilterKind.Gain, true, float.NaN)
    });
    throw new Exception("invalid filter parameter accepted");
}
catch (ArgumentException)
{
    Verify(true, "NaN filter parameters are rejected");
}
var presetTestFolder = Path.Combine(AppContext.BaseDirectory,
    "preset-tests-" + Guid.NewGuid().ToString("N"));
try
{
    var presetStore = new FilterPresetStore(presetTestFolder);
    var presetFilters = new[]
    {
        new FilterStage(Guid.NewGuid(), FilterKind.Gain, true, .7f),
        new FilterStage(Guid.NewGuid(), FilterKind.RnnoiseStandard, false, 0f),
        new FilterStage(Guid.NewGuid(), FilterKind.RnnoiseStandard, true, 0f),
        new FilterStage(Guid.NewGuid(), FilterKind.Limiter, true, -3f),
        new FilterStage(Guid.NewGuid(), FilterKind.Equalizer, Equalizer: new(2f, -3f, 4f)),
        new FilterStage(Guid.NewGuid(), FilterKind.Compressor, false,
            Compressor: new(-21f, 3.5f, 15f, 150f, 2f)),
        new FilterStage(Guid.NewGuid(), FilterKind.PitchShift, true, 2.5f)
    };
    var preset = presetStore.Create("Voice Natural", presetFilters);
    var saved = presetStore.Save(preset);
    Verify(File.Exists(saved.FilePath) && saved.Name == "Voice Natural",
        "Filter preset saves within portable-relative folder");
    var jsonText = File.ReadAllText(saved.FilePath);
    Verify(!jsonText.Contains("EndpointId", StringComparison.OrdinalIgnoreCase) &&
           !jsonText.Contains("SourceId", StringComparison.OrdinalIgnoreCase) &&
           !jsonText.Contains("AudioInput", StringComparison.OrdinalIgnoreCase),
        "Filter presets never serialize input or output endpoints");
    var loaded = FilterPresetStore.ReadFile(saved.FilePath);
    var restored = FilterPresetStore.Restore(loaded);
    Verify(restored.Select(x => x.Kind).SequenceEqual(presetFilters.Select(x => x.Kind)) &&
           restored.Select(x => x.Enabled).SequenceEqual(presetFilters.Select(x => x.Enabled)),
        "Filter presets retain order, repeated types and enabled states");
    Verify(restored.Zip(presetFilters).All(x =>
                x.First.Id != x.Second.Id && x.First with { Id = x.Second.Id } == x.Second),
        "Imported filters receive new instance IDs while preserving parameters");
    Verify(presetStore.List().Count == 1 && presetStore.List()[0].Name == "Voice Natural",
        "Saved presets are automatically discoverable");

    var importedPath = Path.Combine(presetTestFolder, "external.awfilter");
    FilterPresetStore.WriteFile(importedPath, preset);
    Verify(FilterPresetStore.ReadFile(importedPath).Filters.Count == presetFilters.Length,
        "Import/Export share the same strict file format");

    var oldPath = Path.Combine(presetTestFolder, "v1.awfilter");
    File.WriteAllText(oldPath, """
        {"SchemaVersion":1,"Name":"Old Voice","Filters":[{"Kind":"gain","Enabled":true,"Parameter":0.75}]}
        """);
    var oldPreset = FilterPresetStore.Restore(FilterPresetStore.ReadFile(oldPath));
    Verify(oldPreset is [{ Kind: FilterKind.Gain, Parameter: .75f, Equalizer: null, Compressor: null }],
        "schema v1 preset loads without new settings");

    var invalidCompressor = new FilterStage(Guid.NewGuid(), FilterKind.Compressor,
        Compressor: CompressorSettings.Default with { Ratio = float.NaN });
    try
    {
        presetStore.Create("Invalid", [invalidCompressor]);
        throw new Exception("Invalid compressor ratio unexpectedly accepted");
    }
    catch (ArgumentOutOfRangeException)
    {
        Verify(true, "invalid compressor settings are rejected");
    }
    try
    {
        FilterPresetStore.ReadFile(saved.FilePath + ".missing");
        throw new Exception("Missing preset unexpectedly loaded");
    }
    catch (FileNotFoundException)
    {
        Verify(true, "Missing preset file fails without changing current filters");
    }
    try
    {
        File.WriteAllText(Path.Combine(presetTestFolder, "tampered.awfilter"),
            jsonText.Replace($"\"SchemaVersion\": {FilterPresetStore.CurrentSchemaVersion}", "\"SchemaVersion\": 999"));
        FilterPresetStore.ReadFile(Path.Combine(presetTestFolder, "tampered.awfilter"));
        throw new Exception("Unsupported preset schema was accepted");
    }
    catch (InvalidDataException)
    {
        Verify(true, "Future or corrupt preset schema is rejected");
    }
}
finally
{
    if (Directory.Exists(presetTestFolder))
        Directory.Delete(presetTestFolder, recursive: true);
}
Console.WriteLine($"All {count} tests passed.");
