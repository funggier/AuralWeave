using System.Threading;

namespace AuralWeave.Audio.Windows;

/// <summary>
/// Tracks all cooperating AuralWeave user-mode audio sessions for one
/// logical virtual device using a named Windows kernel Event's handle lifetime.
/// Any number of Portable copies may hold it; Windows removes the name when
/// the last handle closes, including after abnormal process termination.
///
/// A management operation must hold VirtualDeviceControlLease while checking
/// IsActive, and session creation must hold the same mutex while opening.
/// This closes the user-mode check/create race (but not a future kernel race).
/// </summary>
public sealed class SharedVirtualAudioActivityLease : IDisposable
{
    private EventWaitHandle? _handle;
    private SharedVirtualAudioActivityLease(EventWaitHandle handle) => _handle = handle;

    private static string EventName(VirtualAudioEndpointRole role) => role switch
    {
        VirtualAudioEndpointRole.Input =>
            @"Global\AuralWeave.VirtualAudio.Input.ActiveSessions.v1",
        VirtualAudioEndpointRole.Output =>
            @"Global\AuralWeave.VirtualAudio.Output.ActiveSessions.v1",
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    public static SharedVirtualAudioActivityLease Open(VirtualAudioEndpointRole role)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Audio activity guard requires Windows.");
        return new SharedVirtualAudioActivityLease(
            new EventWaitHandle(false, EventResetMode.ManualReset, EventName(role)));
    }

    /// <summary>
    /// Returns true while ANY cooperating Portable copy holds a bridge owner
    /// session. Check under a PnP-management mutex before a future install
    /// operation. Permission failures MUST be treated as denial.
    /// </summary>
    public static bool IsActive(VirtualAudioEndpointRole role)
    {
        try
        {
            using var active = EventWaitHandle.OpenExisting(EventName(role));
            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
    }

    public void Dispose() => Interlocked.Exchange(ref _handle, null)?.Dispose();
}
