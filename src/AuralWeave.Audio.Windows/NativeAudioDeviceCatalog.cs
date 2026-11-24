using System.Runtime.InteropServices;
using System.Text;
using AuralWeave.Core;

namespace AuralWeave.Audio.Windows;

/// <summary>
/// Native Windows Core Audio endpoint enumeration. No NAudio or managed audio
/// capture/render implementation is needed; the C++ WASAPI DLL owns device access.
/// Endpoint IDs are stable Windows MMDevice identifiers, not display names.
/// </summary>
public static class NativeAudioDeviceCatalog
{
    private const string Library = "AuralWeave.Native";
    private const int Capacity = 2048;

    public static IReadOnlyList<AudioEndpoint> CaptureDevices()
    {
        var captures = Enumerate(0, AudioEndpointKind.Capture, "capture:");
        var loopbacks = Enumerate(1, AudioEndpointKind.Loopback, "loopback:");
        return captures.Concat(loopbacks)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<AudioEndpoint> RenderDevices() =>
        Enumerate(1, AudioEndpointKind.Render, "")
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    private static IReadOnlyList<AudioEndpoint> Enumerate(
        int endpointKind, AudioEndpointKind modelKind, string idPrefix)
    {
        if (!NativeWasapiAudioRoute.Available)
            throw new InvalidOperationException(
                "AuralWeave.Native.dll is required beside AuralWeave.exe.");

        var count = NativeEndpointCount(endpointKind);
        if (count < 0)
            throw new InvalidOperationException("Native WASAPI endpoint enumeration failed.");

        var devices = new List<AudioEndpoint>(count);
        for (var i = 0; i < count; i++)
        {
            var id = new StringBuilder(Capacity);
            var name = new StringBuilder(Capacity);
            if (NativeEndpointGet(endpointKind, i, id, (nuint)id.Capacity,
                                  name, (nuint)name.Capacity, out var isDefault,
                                  out var sampleRate, out var channels) != 1)
                throw new InvalidOperationException(
                    $"Failed to query native endpoint #{i}. Refresh devices to retry.");
            devices.Add(new AudioEndpoint(idPrefix + id, name.ToString(), modelKind,
                                          isDefault != 0, sampleRate, channels));
        }
        return devices;
    }

    [DllImport(Library, EntryPoint = "aw_endpoint_count",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int NativeEndpointCount(int kind);

    [DllImport(Library, EntryPoint = "aw_endpoint_get",
        CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private static extern int NativeEndpointGet(
        int kind, int index,
        [Out] StringBuilder id, nuint idCapacity,
        [Out] StringBuilder name, nuint nameCapacity,
        out int isDefault, out int sampleRate, out int channels);
}
