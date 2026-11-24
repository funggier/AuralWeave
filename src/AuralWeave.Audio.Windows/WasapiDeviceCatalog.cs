using AuralWeave.Core;
using NAudio.CoreAudioApi;

namespace AuralWeave.Audio.Windows;

public static class WasapiDeviceCatalog
{
    public static IReadOnlyList<AudioEndpoint> CaptureDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = new List<AudioEndpoint>();
        string? defaultCapture = null;
        try { defaultCapture = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia).ID; }
        catch { /* No default input configured. */ }

        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            devices.Add(new AudioEndpoint(
                "capture:" + device.ID,
                device.FriendlyName,
                AudioEndpointKind.Capture,
                device.ID == defaultCapture));
        }

        // A render endpoint can also be captured through WASAPI loopback.
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            devices.Add(new AudioEndpoint("loopback:" + device.ID,
                device.FriendlyName, AudioEndpointKind.Loopback));
        }

        return devices.OrderByDescending(x => x.IsDefault).ThenBy(x => x.DisplayName).ToArray();
    }

    public static IReadOnlyList<AudioEndpoint> RenderDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        string? defaultRender = null;
        try { defaultRender = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID; }
        catch { /* No default output configured. */ }
        var devices = new List<AudioEndpoint>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            devices.Add(new AudioEndpoint(device.ID, device.FriendlyName,
                AudioEndpointKind.Render, device.ID == defaultRender));
        }
        return devices.OrderByDescending(x => x.IsDefault).ThenBy(x => x.Name).ToArray();
    }
}
