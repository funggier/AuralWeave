namespace AuralWeave.Audio.Windows;

/// <summary>
/// Status snapshot for an output endpoint. NaN means buffer duration is not
/// exposed by the native API; do not misrepresent it as zero milliseconds.
/// </summary>
public sealed record OutputTelemetry(
    Guid OutputId,
    double BufferedMilliseconds,
    float Peak,
    string? Error);
