using System.Globalization;

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
    /// <summary>
    /// Always start with the actual Windows endpoint Name, unmodified other
    /// than trimming outer whitespace. Additional metadata follows in braces.
    /// MMDevice IDs are used internally for routing and never shown as labels.
    /// </summary>
    public string DisplayName
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(Name) ? "Audio device" : Name.Trim();
            var metadata = new List<string>(3);

            if (Kind == AudioEndpointKind.Loopback)
                metadata.Add("Loopback");

            if (SampleRate > 0)
            {
                var rate = SampleRate % 1000 == 0
                    ? (SampleRate / 1000).ToString(CultureInfo.InvariantCulture)
                    : (SampleRate / 1000d).ToString("0.###", CultureInfo.InvariantCulture);
                metadata.Add(rate + " kHz");
            }

            if (Channels > 0)
                metadata.Add(Channels switch
                {
                    1 => "Mono",
                    2 => "Stereo",
                    _ => Channels.ToString(CultureInfo.InvariantCulture) + " ch"
                });

            return metadata.Count == 0
                ? name
                : name + " {" + string.Join(" \u00B7 ", metadata) + "}";
        }
    }

    // The WPF ComboBox SelectionBoxItem may call ToString() rather than
    // DisplayMemberPath, so both the dropdown and closed control agree.
    public override string ToString() => DisplayName;
}
