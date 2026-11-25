using System.Threading;

namespace AuralWeave.Audio.Windows;

/// <summary>
/// A machine-wide, folder-independent advisory PnP management mutex.
/// It is held briefly during audio-session startup and deployment, NOT
/// throughout audio playback or recording. Multiple Portable copies
/// can share the same logical Virtual Input and Virtual Output audio.
/// A future privileged driver broker must still enforce authorization.
/// </summary>
/// <remarks>
/// The named Windows mutex is owned by a dedicated thread. This is important:
/// WPF/async callers may Dispose() on a different thread from the caller that
/// acquired the lease. ReleaseMutex must be called by the owning thread.
/// The OS automatically releases abandoned mutexes when a process crashes.
/// The name includes neither a portable directory nor a local user setting.
/// </remarks>
public sealed class VirtualDeviceControlLease : IDisposable
{
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly ManualResetEventSlim _release = new(false);
    private readonly Thread _ownerThread;
    private readonly int _waitMilliseconds;
    private Exception? _initializationError;
    private bool _acquired;
    private int _disposed;

    private VirtualDeviceControlLease(VirtualAudioEndpointRole role, int waitMilliseconds)
    {
        Role = role;
        _waitMilliseconds = waitMilliseconds;
        _ownerThread = new Thread(OwnMutex)
        {
            IsBackground = true,
            Name = $"AuralWeave Virtual {role} control lease"
        };
    }

    public VirtualAudioEndpointRole Role { get; }

    public static VirtualDeviceControlLease? TryAcquire(
        VirtualAudioEndpointRole role, int waitMilliseconds = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(waitMilliseconds);
        if (waitMilliseconds > 2000)
            throw new ArgumentOutOfRangeException(nameof(waitMilliseconds));
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Virtual driver control requires Windows.");
        if (role is not (VirtualAudioEndpointRole.Input or VirtualAudioEndpointRole.Output))
            throw new ArgumentOutOfRangeException(nameof(role));

        var lease = new VirtualDeviceControlLease(role, waitMilliseconds);
        lease._ownerThread.Start();
        lease._ready.Wait();
        if (lease._initializationError is { } error)
        {
            lease.Dispose();
            throw new InvalidOperationException(
                $"Unable to coordinate AuralWeave Virtual {role} control across processes.", error);
        }

        if (!lease._acquired)
        {
            lease.Dispose();
            return null;
        }
        return lease;
    }

    private void OwnMutex()
    {
        // Global\ is shared across Windows logon sessions. The standard
        // DACL restricts access; unauthorized opens fail closed.
        var name = Role == VirtualAudioEndpointRole.Input
            ? @"Global\AuralWeave.VirtualAudio.Input.Controller.v1"
            : @"Global\AuralWeave.VirtualAudio.Output.Controller.v1";
        Mutex? mutex = null;
        var ownsMutex = false;
        try
        {
            mutex = new Mutex(initiallyOwned: false, name);
            try
            {
                ownsMutex = mutex.WaitOne(_waitMilliseconds);
            }
            catch (AbandonedMutexException)
            {
                // The previous process ended unexpectedly. Windows has
                // atomically transferred ownership to this thread.
                ownsMutex = true;
            }
            _acquired = ownsMutex;
        }
        catch (Exception ex)
        {
            _initializationError = ex;
        }
        finally
        {
            _ready.Set();
        }

        try
        {
            if (ownsMutex)
                _release.Wait();
        }
        finally
        {
            if (ownsMutex)
                mutex!.ReleaseMutex();
            mutex?.Dispose();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _release.Set();
        if (Thread.CurrentThread != _ownerThread)
            _ownerThread.Join();
        _release.Dispose();
        _ready.Dispose();
    }
}
