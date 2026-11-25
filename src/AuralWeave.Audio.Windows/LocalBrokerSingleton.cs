using System.Threading;

namespace AuralWeave.Audio.Windows;

/// <summary>
/// Coordinates exactly one non-elevated mock broker per Windows logon session.
/// The mutex is acquired and released on the same dedicated owning thread.
/// Not a security boundary: another process running as this user can cause a
/// denial of service. No machine-wide driver ownership is implied.
/// </summary>
public sealed class LocalBrokerSingleton : IDisposable
{
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly ManualResetEventSlim _release = new(false);
    private readonly Thread _owner;
    private Exception? _error;
    private bool _acquired;
    private int _disposed;

    private LocalBrokerSingleton()
    {
        _owner = new Thread(Own)
        {
            IsBackground = true,
            Name = "AuralWeave mock broker singleton"
        };
    }

    public static LocalBrokerSingleton Acquire()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("AuralWeave local broker requires Windows.");
        var lease = new LocalBrokerSingleton();
        lease._owner.Start();
        lease._ready.Wait();
        if (lease._error is { } error)
        {
            lease.Dispose();
            throw new InvalidOperationException("Cannot acquire mock broker singleton.", error);
        }
        if (!lease._acquired)
        {
            lease.Dispose();
            throw new InvalidOperationException("AuralWeave mock broker is already active in this session.");
        }
        return lease;
    }

    private void Own()
    {
        Mutex? mutex = null;
        var owns = false;
        try
        {
            mutex = new Mutex(false, @"Local\AuralWeave.MockBroker.Singleton.v1");
            try { owns = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { owns = true; }
            _acquired = owns;
        }
        catch (Exception ex) { _error = ex; }
        finally { _ready.Set(); }

        try
        {
            if (owns)
                _release.Wait();
        }
        finally
        {
            if (owns)
                mutex!.ReleaseMutex();
            mutex?.Dispose();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _release.Set();
        if (Thread.CurrentThread != _owner)
            _owner.Join();
        _ready.Dispose();
        _release.Dispose();
    }
}
