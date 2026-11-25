using System.Globalization;

namespace AuralWeave.Audio.Windows;

/// <summary>
/// Strict, typed snapshot of the non-privileged Mock Broker's control status.
/// Suitable for diagnostics or a future read-only Studio status indicator.
/// No field asserts that a Windows kernel audio endpoint is installed.
/// </summary>
public sealed record LocalBrokerRuntimeStatus(
    Guid BrokerInstanceId, int MicrophoneSources, int SpeakerListeners,
    long PumpTicks, ulong RoutedFrames, long ReceivedPings,
    ulong DroppedListenerFrames)
{
    public static LocalBrokerRuntimeStatus Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            throw new InvalidDataException("Empty mock broker STATUS response.");

        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 9 || parts[0] != "OK" || parts[1] != "STATUS")
            throw new InvalidDataException("Incompatible mock broker STATUS response.");

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 2; i < parts.Length; i++)
        {
            var index = parts[i].IndexOf('=');
            if (index is < 1 || index == parts[i].Length - 1 ||
                !fields.TryAdd(parts[i][..index], parts[i][(index + 1)..]))
                throw new InvalidDataException("Invalid mock broker STATUS field.");
        }
        if (fields.Count != 7 ||
            !fields.TryGetValue("instance", out var instance) ||
            !Guid.TryParseExact(instance, "N", out var id) ||
            id == Guid.Empty ||
            !TryInt(fields, "input", out var mic) || mic > LocalVirtualAudioBroker.MaxClients ||
            !TryInt(fields, "output", out var speaker) || speaker > LocalVirtualAudioBroker.MaxClients ||
            !TryLong(fields, "ticks", out var ticks) ||
            !TryULong(fields, "frames", out var frames) ||
            !TryLong(fields, "pings", out var pings) ||
            !TryULong(fields, "dropped", out var dropped))
            throw new InvalidDataException("Invalid mock broker STATUS values.");

        return new LocalBrokerRuntimeStatus(id, mic, speaker, ticks,
            frames, pings, dropped);
    }

    private static bool TryInt(Dictionary<string, string> fields, string key, out int result)
    {
        result = 0;
        return fields.TryGetValue(key, out var value) &&
            int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryLong(Dictionary<string, string> fields, string key, out long result)
    {
        result = 0;
        return fields.TryGetValue(key, out var value) &&
            long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryULong(Dictionary<string, string> fields, string key, out ulong result)
    {
        result = 0;
        return fields.TryGetValue(key, out var value) &&
            ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);
    }
}
