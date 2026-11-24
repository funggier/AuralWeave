namespace AuralWeave.Core;

public sealed record OutputRoute(
    Guid Id,
    string EndpointId,
    float Gain = 1f,
    bool Muted = false);

public sealed record AudioEffectInstance(
    Guid Id,
    string TypeId,
    bool Enabled = true,
    float Gain = 1f);

public static class AudioRouteRules
{
    public const string GainEffect = "gain";

    public static IReadOnlyList<AudioEndpoint> AvailableOutputs(
        IEnumerable<AudioEndpoint> discovered,
        IEnumerable<OutputRoute> selected)
    {
        ArgumentNullException.ThrowIfNull(discovered);
        ArgumentNullException.ThrowIfNull(selected);
        var selectedIds = selected.Select(x => x.EndpointId)
            .ToHashSet(StringComparer.Ordinal);
        return discovered.Where(d => d.Kind == AudioEndpointKind.Render &&
                                      !selectedIds.Contains(d.Id))
                         .ToArray();
    }

    public static void ValidateOutputs(IEnumerable<OutputRoute> outputs)
    {
        ArgumentNullException.ThrowIfNull(outputs);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var output in outputs)
        {
            if (string.IsNullOrWhiteSpace(output.EndpointId))
                throw new ArgumentException("An output endpoint ID cannot be empty.");
            if (!ids.Add(output.EndpointId))
                throw new ArgumentException("The same output endpoint cannot appear twice in a route.");
            if (!float.IsFinite(output.Gain) || output.Gain < 0 || output.Gain > 2)
                throw new ArgumentOutOfRangeException(nameof(outputs), "Output gain must be in the range 0..2.");
        }
    }

    public static void ValidateEffects(IEnumerable<AudioEffectInstance> effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        var ids = new HashSet<Guid>();
        foreach (var effect in effects)
        {
            if (effect.Id == Guid.Empty || !ids.Add(effect.Id))
                throw new ArgumentException("Effect instance IDs must be unique and nonempty.");
            if (effect.TypeId != GainEffect)
                throw new ArgumentException($"Unsupported effect type: {effect.TypeId}");
            if (!float.IsFinite(effect.Gain) || effect.Gain < 0 || effect.Gain > 2)
                throw new ArgumentOutOfRangeException(nameof(effects), "Effect gain must be in the range 0..2.");
        }
    }
}
