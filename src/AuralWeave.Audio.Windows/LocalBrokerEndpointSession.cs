namespace AuralWeave.Audio.Windows;

/// <summary>
/// Owns both sides of one same-user developer MOCK broker registration:
/// a fresh native PCM ring and its dedicated control pipe. Opening a new
/// session after broker restart creates an EMPTY ring, never replays PCM
/// queued for the previous process. This does not install an audio endpoint.
/// </summary>
public sealed class LocalBrokerEndpointSession : IAsyncDisposable
{
    private int _disposed;

    private LocalBrokerEndpointSession(
        NativeVirtualAudioBridge pcm, LocalBrokerControlClient control)
    {
        Pcm = pcm;
        Control = control;
    }

    public NativeVirtualAudioBridge Pcm { get; }
    public LocalBrokerControlClient Control { get; }
    public Guid BrokerInstanceId => Control.BrokerInstanceId;

    public static async Task<LocalBrokerEndpointSession> OpenAsync(
        uint role, CancellationToken cancellationToken = default)
    {
        if (role is not (1u or 2u))
            throw new ArgumentOutOfRangeException(nameof(role));

        // Never attach an old/shared mapping to a restarted broker. Each
        // explicit connection uses a fresh native SPSC owner and FIFO.
        var pcm = NativeVirtualAudioBridge.Create(role, 1, 48000, 2, 2048);
        try
        {
            var control = await LocalBrokerControlClient.ConnectAsync(
                pcm, cancellationToken).ConfigureAwait(false);
            return new LocalBrokerEndpointSession(pcm, control);
        }
        catch
        {
            pcm.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Explicit fail-closed reconnect only after the previous session is
    /// disposed and a new mock broker has been launched. Does not auto-start
    /// a process, retry forever or resend stale samples.
    /// </summary>
    public static async Task<LocalBrokerEndpointSession> OpenAfterRestartAsync(
        uint role, Guid previousInstanceId,
        CancellationToken cancellationToken = default)
    {
        if (previousInstanceId == Guid.Empty)
            throw new ArgumentException("Previous broker instance is missing.",
                nameof(previousInstanceId));

        var session = await OpenAsync(role, cancellationToken).ConfigureAwait(false);
        if (session.BrokerInstanceId != previousInstanceId)
            return session;

        await session.DisposeAsync().ConfigureAwait(false);
        throw new InvalidOperationException(
            "Broker instance has not changed; refusing a fake restart.");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            await Control.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            Pcm.Dispose();
        }
    }
}
