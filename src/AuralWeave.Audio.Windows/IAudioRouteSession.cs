using AuralWeave.Core;

namespace AuralWeave.Audio.Windows;

/// <summary>Shared control-plane contract for the managed and native WASAPI backends.</summary>
public interface IAudioRouteSession : IDisposable
{
    bool IsRunning { get; }
    long CapturedFrames { get; }
    float InputPeak { get; }
    string? LastError { get; }
    long DroppedRenderFrames { get; }
    long UnderrunRenderFrames { get; }
    void ReplaceEffects(IEnumerable<AudioEffectInstance> effects);
    void Start(string sourceId, IReadOnlyList<OutputRoute> outputs);
    void Stop();
    void SetOutputLevel(Guid outputId, float gain, bool muted);
    IReadOnlyList<OutputTelemetry> GetOutputTelemetry();
}
