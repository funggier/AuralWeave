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
Console.WriteLine($"All {count} tests passed.");
