namespace AuralWeave.Audio.Windows;

/// <summary>
/// Opt-in, DEVELOPMENT-ONLY user-mode scheduling reference for a future
/// privileged Virtual Audio broker. It does not install, open, sign, start
/// or configure a Windows kernel driver. The caller supplies an already
/// authorized VirtualKernelPcmClient and independent native PCM mappings.
///
/// PeriodicTimer intentionally coalesces missed ticks rather than replaying
/// an unbounded burst of stale audio. Windows timer granularity makes this
/// unsuitable for production low-latency clocking: a signed, isolated broker
/// will need WASAPI/KS event-driven timing and drift measurement.
/// </summary>
public sealed class VirtualKernelAudioWorker : IAsyncDisposable
{
    private const uint ExpectedRate = 48000;
    private const uint ExpectedChannels = 2;
    private readonly VirtualKernelPcmClient _driver;
    private readonly VirtualKernelAudioPump _pump;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly TimeSpan _interval;
    private Task? _execution;
    private Exception? _lastFailure;
    private bool _disposed;
    private long _cycles;
    private long _activeCycles;

    public VirtualKernelAudioWorker(VirtualKernelPcmClient driver,
        VirtualKernelAudioPump pump)
    {
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        _pump = pump ?? throw new ArgumentNullException(nameof(pump));
        if (pump.BlockFrames is < 1 or > VirtualKernelPcmProtocol.MaxFrames)
            throw new ArgumentOutOfRangeException(nameof(pump));
        _interval = TimeSpan.FromSeconds(pump.BlockFrames / (double)ExpectedRate);
    }

    public long Cycles => Interlocked.Read(ref _cycles);
    public long ActiveCycles => Interlocked.Read(ref _activeCycles);
    public Exception? LastFailure => Volatile.Read(ref _lastFailure);

    /// <summary>The loop task, for observing failures. Never fire-and-forget
    /// this property in a privileged host; observe it or call StopAsync.</summary>
    public Task Completion
    {
        get { lock (_gate) return _execution ?? Task.CompletedTask; }
    }

    public bool IsRunning
    {
        get { lock (_gate) return _execution is { IsCompleted: false }; }
    }

    /// <summary>
    /// Explicit start only; never called by the ordinary Studio app.
    /// Fail closed if both virtual WaveRT endpoints are not currently active
    /// at the exact 48 kHz/stereo/signed-PCM format of the v1 transport.
    /// </summary>
    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_execution is not null || _stop.IsCancellationRequested)
                throw new InvalidOperationException(
                    "Virtual PCM worker is single-use; construct another to restart.");

            RequireActive(VirtualKernelPcmProtocol.InputRole,
                VirtualKernelPcmProtocol.Signed32Format, 32);
            RequireActive(VirtualKernelPcmProtocol.OutputRole,
                VirtualKernelPcmProtocol.Signed16Format, 16);
            _execution = Task.Run(() => RunLoopAsync(_stop.Token));
        }
    }

    private void RequireActive(uint role, uint format, uint bits)
    {
        var actual = _driver.Query(role);
        if (actual.SampleRate != ExpectedRate ||
            actual.Channels != ExpectedChannels ||
            actual.Format != format || actual.Bits != bits)
            throw new InvalidOperationException(
                "Virtual PCM kernel stream is absent or has an incompatible format.");
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken)
                       .ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                // One synchronous bounded IOCTL read/write pair per tick.
                // A failed exchange stops this worker; no busy retry storm.
                var frames = _pump.PumpOnce();
                Interlocked.Increment(ref _cycles);
                if (frames > 0) Interlocked.Increment(ref _activeCycles);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal explicit Stop/Dispose.
        }
        catch (Exception error)
        {
            Volatile.Write(ref _lastFailure, error);
            throw;
        }
    }

    /// <summary>
    /// Request stop and await the last synchronous transfer. A blocked
    /// DeviceIoControl cannot be forcibly canceled by this user-mode loop;
    /// production broker must use bounded or cancelable I/O.
    /// </summary>
    public async Task StopAsync()
    {
        Task? execution;
        lock (_gate)
        {
            if (!_disposed) _stop.Cancel();
            execution = _execution;
        }
        if (execution is not null)
            await execution.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        Task? execution;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _stop.Cancel();
            execution = _execution;
        }
        try
        {
            if (execution is not null)
                await execution.ConfigureAwait(false);
        }
        catch (Exception) when (LastFailure is not null)
        {
            // StopAsync and Completion expose the original failure.
            // Disposal still releases the timer/cancellation resources.
        }
        finally
        {
            _stop.Dispose();
        }
    }
}
