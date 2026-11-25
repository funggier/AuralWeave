namespace AuralWeave.Audio.Windows;

/// <summary>
/// Machine-wide serialization of the elevated setup transaction. Studio audio
/// and the mock broker do not acquire this mutex.
/// </summary>
public static class VirtualDriverInstallLock
{
    public static int Run(TimeSpan timeout, Func<int> install)
    {
        ArgumentNullException.ThrowIfNull(install);
        using var mutex = new Mutex(false, @"Global\AuralWeave.VirtualAudio.DriverInstall.v1");
        try
        {
            if (!mutex.WaitOne(timeout))
                throw new TimeoutException("Another AuralWeave Driver Setup is running.");
        }
        catch (AbandonedMutexException)
        {
            // Windows transferred ownership to this thread. The caller
            // still must inspect PnP, including nodes left by the prior setup.
        }

        try
        {
            return install();
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
