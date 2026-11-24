namespace AuralWeave.Core;

public enum AudioEndpointKind
{
    Capture,
    Loopback,
    Render
}

public sealed record AudioEndpoint(
    string Id,
    string Name,
    AudioEndpointKind Kind,
    bool IsDefault = false,
    int SampleRate = 0,
    int Channels = 0)
{
    public string DisplayName => Kind == AudioEndpointKind.Loopback
        ? $"{Name} (Loopback)"
        : Name;
}
