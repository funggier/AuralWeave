using System.Collections.Immutable;

namespace AuralWeave.Core;

/// <summary>Audio filter kinds in their native processing order (C ABI values).</summary>
public enum FilterKind
{
    None = 0,
    Gain = 1,
    RnnoiseStandard = 2,
    NoiseGate = 3,
    Limiter = 4,
    Equalizer = 5,
    Compressor = 6,
    PitchShift = 7, // Reserved for reading v0.4.0 presets.
    Equalizer5Band = 8,
    Equalizer7Band = 9,
    Vst2 = 10
}

public sealed record EqualizerSettings(float LowDb, float MidDb, float HighDb)
{
    public static EqualizerSettings Flat { get; } = new(0f, 0f, 0f);
}

public sealed record CompressorSettings(
    float ThresholdDb, float Ratio, float AttackMs, float ReleaseMs, float MakeupDb)
{
    public static CompressorSettings Default { get; } = new(-18f, 3f, 10f, 100f, 0f);
}

public sealed record VstPluginReference(string Path, string Sha256)
{
    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(Path) && Path.Length <= 32767 &&
        !Path.Any(char.IsControl) &&
        (Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
         Path.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase)) &&
        Sha256 is { Length: 64 } && Sha256.All(Uri.IsHexDigit);
}

public sealed record FilterStage(
    Guid Id,
    FilterKind Kind = FilterKind.None,
    bool Enabled = true,
    float Parameter = 0f,
    EqualizerSettings? Equalizer = null,
    CompressorSettings? Compressor = null,
    ImmutableArray<float>? BandGainsDb = null,
    VstPluginReference? VstPlugin = null,
    bool VstCommonFiles = false)
{
    public static int ExtendedEqualizerBandCount(FilterKind kind) => kind switch
    {
        FilterKind.Equalizer5Band => 5,
        FilterKind.Equalizer7Band => 7,
        _ => 0
    };

    public static float DefaultParameter(FilterKind kind) => kind switch
    {
        FilterKind.Gain => 1f,
        FilterKind.NoiseGate => -45f,
        FilterKind.Limiter => -1f,
        _ => 0f
    };
}

public static class FilterStageRules
{
    public static void Validate(IEnumerable<FilterStage> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        var ids = new HashSet<Guid>();
        var count = 0;
        foreach (var stage in stages)
        {
            if (!ids.Add(stage.Id)) throw new ArgumentException("Duplicate filter instance ID.");
            if (++count > 64) throw new ArgumentException("At most 64 filters are supported.");
            var bandCount = FilterStage.ExtendedEqualizerBandCount(stage.Kind);
            if (!Enum.IsDefined(stage.Kind) || !float.IsFinite(stage.Parameter) ||
                (stage.Equalizer is not null && stage.Kind is not FilterKind.Equalizer) ||
                (stage.Compressor is not null && stage.Kind is not FilterKind.Compressor) ||
                (stage.BandGainsDb is not null && bandCount == 0) ||
                (stage.VstCommonFiles && stage.Kind != FilterKind.Vst2) ||
                (stage.VstPlugin is { } plugin && (stage.Kind != FilterKind.Vst2 || !plugin.IsValid())))
                throw new ArgumentException("Invalid filter configuration.");

            var eq = stage.Equalizer ?? EqualizerSettings.Flat;
            var compressor = stage.Compressor ?? CompressorSettings.Default;
            var valid = stage.Kind switch
            {
                FilterKind.None => true,
                FilterKind.Vst2 => stage.Parameter == 0f,
                FilterKind.Gain => stage.Parameter is >= 0f and <= 2f,
                FilterKind.RnnoiseStandard => stage.Parameter == 0f,
                FilterKind.NoiseGate => stage.Parameter is >= -80f and <= 0f,
                FilterKind.Limiter => stage.Parameter is >= -24f and <= 0f,
                FilterKind.Equalizer => stage.Parameter == 0f &&
                    eq.LowDb is >= -12f and <= 12f &&
                    eq.MidDb is >= -12f and <= 12f &&
                    eq.HighDb is >= -12f and <= 12f,
                FilterKind.Equalizer5Band or FilterKind.Equalizer7Band => stage.Parameter == 0f &&
                    (stage.BandGainsDb is not { } bands ||
                     (!bands.IsDefault && bands.Length == bandCount &&
                      bands.All(db => db is >= -12f and <= 12f))),
                FilterKind.Compressor => stage.Parameter == 0f &&
                    compressor.ThresholdDb is >= -60f and <= 0f &&
                    compressor.Ratio is >= 1f and <= 20f &&
                    compressor.AttackMs is >= 1f and <= 100f &&
                    compressor.ReleaseMs is >= 10f and <= 1000f &&
                    compressor.MakeupDb is >= 0f and <= 24f,
                _ => false
            };
            if (!valid) throw new ArgumentOutOfRangeException(
                nameof(stages), "Filter parameter is out of range.");
        }
    }
}
