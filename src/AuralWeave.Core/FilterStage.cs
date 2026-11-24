namespace AuralWeave.Core;

/// <summary>Audio filter kinds in their native processing order (C ABI values).</summary>
public enum FilterKind
{
    None = 0,
    Gain = 1,
    RnnoiseStandard = 2,
    NoiseGate = 3,
    Limiter = 4
}

public sealed record FilterStage(
    Guid Id,
    FilterKind Kind = FilterKind.None,
    bool Enabled = true,
    float Parameter = 0f)
{
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
            if (!Enum.IsDefined(stage.Kind) || float.IsNaN(stage.Parameter) ||
                float.IsInfinity(stage.Parameter))
                throw new ArgumentException("Invalid filter configuration.");
            var valid = stage.Kind switch
            {
                FilterKind.None => true,
                FilterKind.Gain => stage.Parameter is >= 0f and <= 2f,
                FilterKind.RnnoiseStandard => stage.Parameter == 0f,
                FilterKind.NoiseGate => stage.Parameter is >= -80f and <= 0f,
                FilterKind.Limiter => stage.Parameter is >= -24f and <= 0f,
                _ => false
            };
            if (!valid) throw new ArgumentOutOfRangeException(
                nameof(stages), "Filter parameter is out of range.");
        }
    }
}
