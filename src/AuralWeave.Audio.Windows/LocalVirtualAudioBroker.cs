using System.IO.Pipes;
using System.Text;
using System.Text.RegularExpressions;

namespace AuralWeave.Audio.Windows;

/// <summary>
/// Explicitly launched, NON-ELEVATED, same-Windows-user Named Pipe broker.
/// It performs ONLY user-mode PCM loopback for developer testing:
/// client microphone contributions -> input mix -> output fanout.
/// There is deliberately no access to DeviceIoControl, admin privileges,
/// Windows services, driver setup, or actual virtual audio endpoints.
///
/// The pipe is CurrentUserOnly. It is not a security boundary for a future
/// elevated kernel broker, which will require explicit client identity,
/// ACL/impersonation policy, and protocol review before use with real PCM.
/// </summary>
public sealed class LocalVirtualAudioBroker : IDisposable
{
    public const string PipeName = "AuralWeave.MockBroker.Control.v2";
    public const int MaxClients = 8;
    private const int FramesPerBlock = 128;
    private const int MaxCommandLength = 200;
    private readonly Guid _instanceId = Guid.NewGuid();
    private readonly object _gate = new();
    private readonly SharedVirtualAudioBus _input = new(1, 48000, 2, FramesPerBlock);
    private readonly SharedVirtualAudioBus _output = new(2, 48000, 2, FramesPerBlock);
    private readonly List<IDisposable> _owned = [];
    private readonly NativeVirtualAudioBridge _micSourceSink;
    private readonly NativeVirtualAudioBridge _speakerSource;
    private readonly NativeVirtualAudioBridge _micReader;
    private readonly NativeVirtualAudioBridge _speakerWriter;
    private readonly float[] _samples = new float[FramesPerBlock * 2];
    private int _started;
    private bool _disposed;
    private long _ticks;
    private long _pings;
    private ulong _frames;

    private static readonly Regex InputMap = new(
        @"\ALocal\\AuralWeave\.PCM\.v1\.IN\.1\.\{[0-9A-Fa-f-]{36}\}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex OutputMap = new(
        @"\ALocal\\AuralWeave\.PCM\.v1\.OUT\.1\.\{[0-9A-Fa-f-]{36}\}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public LocalVirtualAudioBroker()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The local virtual broker requires Windows.");
        try
        {
            _micSourceSink = Own(NativeVirtualAudioBridge.Create(1, 1, 48000, 2, 2048));
            _speakerSource = Own(NativeVirtualAudioBridge.Create(2, 1, 48000, 2, 2048));
            _micReader = Own(NativeVirtualAudioBridge.Attach(_micSourceSink.Name, 1, 1));
            _speakerWriter = Own(NativeVirtualAudioBridge.Attach(_speakerSource.Name, 2, 1));

            _input.AddListener(_micSourceSink.Name);
            _output.AddContributor(_speakerSource.Name);
        }
        catch
        {
            for (var i = _owned.Count - 1; i >= 0; i--)
                _owned[i].Dispose();
            _input.Dispose();
            _output.Dispose();
            throw;
        }
    }

    private T Own<T>(T value) where T : IDisposable
    {
        _owned.Add(value);
        return value;
    }

    /// <summary>
    /// A broker owns all NativeVirtualAudioBridge handles and serialized
    /// membership updates. Cancellation tears down active pipe sessions.
    /// RunAsync is single-use and should be awaited before Dispose.
    /// </summary>
    public async Task RunAsync(CancellationToken shutdown)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("Local mock broker cannot be restarted.");
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var singleton = LocalBrokerSingleton.Acquire();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        using var slots = new SemaphoreSlim(MaxClients, MaxClients);
        var clients = new List<Task>();
        var pumpTask = PumpLoopAsync(stop.Token);
        // Stop accepting new clients immediately if the audio pump faults.
        // Otherwise the control plane could report healthy while PCM stalls.
        _ = pumpTask.ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                try { stop.Cancel(); }
                catch (ObjectDisposedException) { }
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await slots.WaitAsync(stop.Token).ConfigureAwait(false);
                NamedPipeServerStream? server = null;
                try
                {
                    server = new NamedPipeServerStream(
                        PipeName, PipeDirection.InOut, MaxClients,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(stop.Token)
                        .ConfigureAwait(false);
                    var active = server;
                    server = null;
                    clients.RemoveAll(task => task.IsCompleted);
                    clients.Add(ServeClientAsync(active, slots, stop.Token));
                }
                finally
                {
                    if (server is not null)
                    {
                        server.Dispose();
                        slots.Release();
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally
        {
            stop.Cancel();
            await Task.WhenAll(clients).ConfigureAwait(false);
            await pumpTask.ConfigureAwait(false);
        }
    }

    private async Task PumpLoopAsync(CancellationToken shutdown)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(shutdown).ConfigureAwait(false))
            {
                lock (_gate)
                {
                    // Mock loopback ONLY, never a kernel or Windows audio device.
                    _input.Pump();
                    var frames = _micReader.Read(_samples);
                    if (frames != 0)
                        _speakerWriter.Write(_samples, frames);
                    _output.Pump();
                    _ticks++;
                    _frames += frames;
                }
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
    }

    private async Task ServeClientAsync(NamedPipeServerStream pipe,
        SemaphoreSlim slots, CancellationToken shutdown)
    {
        string? mapping = null;
        bool microphone = false;
        bool greeted = false;
        try
        {
            using (pipe)
            {
                while (!shutdown.IsCancellationRequested && pipe.IsConnected)
                {
                    // Drop silent clients even when their PCM owner is still
                    // alive; requiring an explicit heartbeat bounds stale IPC.
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
                    deadline.CancelAfter(TimeSpan.FromSeconds(15));
                    string? request;
                    try
                    {
                        request = await ReadLineAsync(pipe, deadline.Token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!shutdown.IsCancellationRequested)
                    {
                        break; // Lost heartbeat.
                    }
                    if (request is null)
                        break;
                    // Explicit version negotiation on every new connection.
                    // An old client cannot accidentally register PCM under a
                    // changed wire protocol after a broker replacement.
                    if (!greeted)
                    {
                        if (request != "HELLO 2")
                        {
                            await ReplyAsync(pipe, "ERR PROTOCOL_VERSION", shutdown)
                                .ConfigureAwait(false);
                            break;
                        }
                        greeted = true;
                        await ReplyAsync(pipe,
                            $"OK HELLO 2 instance={_instanceId:N}", shutdown)
                            .ConfigureAwait(false);
                        continue;
                    }
                    if (request == "PING")
                    {
                        lock (_gate) _pings++;
                        await ReplyAsync(pipe, "OK PONG", shutdown).ConfigureAwait(false);
                        continue;
                    }
                    if (request == "STATUS")
                    {
                        string status;
                        lock (_gate)
                            status = $"OK STATUS input={_input.Contributors} output={_output.Listeners} ticks={_ticks} frames={_frames} instance={_instanceId:N} pings={_pings} dropped={_output.FramesDropped}";
                        await ReplyAsync(pipe, status, shutdown).ConfigureAwait(false);
                        continue;
                    }
                    if (request == "QUIT")
                    {
                        await ReplyAsync(pipe, "OK BYE", shutdown).ConfigureAwait(false);
                        break;
                    }
                    if (request.StartsWith("REGISTER ", StringComparison.Ordinal)
                        && mapping is null)
                    {
                        var parts = request.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length != 3 ||
                            !((parts[1] == "INPUT" && InputMap.IsMatch(parts[2])) ||
                              (parts[1] == "OUTPUT" && OutputMap.IsMatch(parts[2]))))
                        {
                            await ReplyAsync(pipe, "ERR INVALID_MAPPING", shutdown)
                                .ConfigureAwait(false);
                            continue;
                        }
                        try
                        {
                            lock (_gate)
                            {
                                if (parts[1] == "INPUT")
                                    _input.AddContributor(parts[2]);
                                else
                                    _output.AddListener(parts[2]);
                            }
                            microphone = parts[1] == "INPUT";
                            mapping = parts[2];
                            await ReplyAsync(pipe, "OK REGISTERED " + parts[1], shutdown)
                                .ConfigureAwait(false);
                        }
                        catch (Exception error) when (error is
                            IOException or InvalidOperationException or ArgumentException)
                        {
                            await ReplyAsync(pipe, "ERR ATTACH_REJECTED", shutdown)
                                .ConfigureAwait(false);
                        }
                        continue;
                    }
                    await ReplyAsync(pipe, "ERR INVALID_COMMAND", shutdown)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (Exception error) when (error is IOException or
            EndOfStreamException or OperationCanceledException or
            ObjectDisposedException or InvalidDataException)
        {
            // Client disconnection or bad wire input is isolated to its
            // connection; one malformed peer cannot stop other clients.
        }
        finally
        {
            if (mapping is not null)
            {
                lock (_gate)
                {
                    if (microphone)
                        _input.RemoveContributor(mapping);
                    else
                        _output.RemoveListener(mapping);
                }
            }
            pipe.Dispose();
            slots.Release();
        }
    }

    private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken token)
    {
        var buffer = new byte[MaxCommandLength];
        var one = new byte[1];
        var length = 0;
        while (true)
        {
            var got = await stream.ReadAsync(one.AsMemory(), token).ConfigureAwait(false);
            if (got == 0)
            {
                if (length != 0)
                    throw new InvalidDataException("Truncated broker command.");
                return null;
            }
            if (one[0] == (byte)'\n')
                return Encoding.ASCII.GetString(buffer, 0, length);
            if (one[0] == (byte)'\r')
                continue;
            if (one[0] < 32 || one[0] > 126 || length == MaxCommandLength)
                throw new InvalidDataException("Non-ASCII or oversized broker command.");
            buffer[length++] = one[0];
        }
    }

    private static async Task ReplyAsync(Stream stream, string reply, CancellationToken token)
    {
        var bytes = Encoding.ASCII.GetBytes(reply + "\n");
        await stream.WriteAsync(bytes.AsMemory(), token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _input.Dispose();
        _output.Dispose();
        for (var i = _owned.Count - 1; i >= 0; i--)
            _owned[i].Dispose();
        _owned.Clear();
    }
}
