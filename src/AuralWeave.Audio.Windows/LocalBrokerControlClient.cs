using System.IO.Pipes;
using System.Text;

namespace AuralWeave.Audio.Windows;

/// <summary>
/// Non-privileged prototype control client for the SAME-USER MOCK broker.
/// Owns only a Named Pipe connection; the caller continues to own and dispose
/// its own NativeVirtualAudioBridge. An automatic heartbeat bounds stale
/// registrations. No actual Windows virtual endpoint is created or installed.
/// </summary>
public sealed class LocalBrokerControlClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly SemaphoreSlim _requests = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _heartbeat;
    private Exception? _failure;
    private int _disposed;

    private LocalBrokerControlClient(NamedPipeClientStream pipe)
    {
        _pipe = pipe;
        _heartbeat = HeartbeatLoopAsync();
    }

    public Exception? LastFailure => Volatile.Read(ref _failure);

    /// <summary>
    /// Register one independently owned PCM mapping. Studio clients must
    /// keep the mapping open until AFTER disposing this control connection.
    /// </summary>
    public static async Task<LocalBrokerControlClient> ConnectAsync(
        NativeVirtualAudioBridge ownedMapping,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ownedMapping);
        if (ownedMapping.Slot != 1 ||
            ownedMapping.Role is not (VirtualKernelPcmProtocol.InputRole or
                                       VirtualKernelPcmProtocol.OutputRole))
            throw new ArgumentException("Expected single-slot Input/Output owner mapping.",
                nameof(ownedMapping));

        var pipe = new NamedPipeClientStream(".", LocalVirtualAudioBroker.PipeName,
            PipeDirection.InOut, PipeOptions.Asynchronous);
        LocalBrokerControlClient? result = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            var role = ownedMapping.Role == VirtualKernelPcmProtocol.InputRole
                ? "INPUT" : "OUTPUT";
            var response = await ExchangeAsync(pipe,
                "REGISTER " + role + " " + ownedMapping.Name,
                timeout.Token).ConfigureAwait(false);
            if (response != "OK REGISTERED " + role)
                throw new InvalidOperationException(
                    "Mock broker rejected client mapping: " + response);
            result = new LocalBrokerControlClient(pipe);
            return result;
        }
        catch
        {
            if (result is not null)
                await result.DisposeAsync().ConfigureAwait(false);
            else
                pipe.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Explicit status probe. The heartbeat keeps the registration open,
    /// but does not automatically recreate mappings after broker failure.
    /// </summary>
    public Task<string> QueryStatusAsync(CancellationToken token = default) =>
        RequestAsync("STATUS", token);

    public Task<string> PingAsync(CancellationToken token = default) =>
        RequestAsync("PING", token);

    private async Task<string> RequestAsync(string command, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            _stop.Token, token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await _requests.WaitAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            var response = await ExchangeAsync(_pipe, command, timeout.Token)
                .ConfigureAwait(false);
            if (!response.StartsWith("OK ", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Mock broker rejected " + command + ": " + response);
            return response;
        }
        finally
        {
            _requests.Release();
        }
    }

    private async Task HeartbeatLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(4));
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token)
                .ConfigureAwait(false))
            {
                if (await RequestAsync("PING", _stop.Token).ConfigureAwait(false) != "OK PONG")
                    throw new InvalidDataException("Unexpected mock broker heartbeat response.");
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Volatile.Write(ref _failure, ex);
        }
    }

    private static async Task<string> ExchangeAsync(Stream pipe,
        string command, CancellationToken token)
    {
        if (command.Length is < 1 or > 200 ||
            command.Any(ch => ch is < ' ' or > '~'))
            throw new ArgumentException("Invalid bounded ASCII broker command.",
                nameof(command));
        var bytes = Encoding.ASCII.GetBytes(command + "\n");
        await pipe.WriteAsync(bytes.AsMemory(), token).ConfigureAwait(false);
        await pipe.FlushAsync(token).ConfigureAwait(false);

        var single = new byte[1];
        var reply = new byte[200];
        var count = 0;
        while (true)
        {
            var read = await pipe.ReadAsync(single.AsMemory(), token)
                .ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("Mock broker closed control connection.");
            if (single[0] == '\n')
                return Encoding.ASCII.GetString(reply, 0, count);
            if (single[0] == '\r')
                continue;
            if (single[0] < 32 || single[0] > 126 || count == reply.Length)
                throw new InvalidDataException("Invalid or oversized mock broker response.");
            reply[count++] = single[0];
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _stop.Cancel();
        try { await _heartbeat.ConfigureAwait(false); }
        catch (OperationCanceledException) { }

        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await _requests.WaitAsync(deadline.Token).ConfigureAwait(false);
            try
            {
                if (_pipe.IsConnected)
                    await ExchangeAsync(_pipe, "QUIT", deadline.Token)
                        .ConfigureAwait(false);
            }
            finally { _requests.Release(); }
        }
        catch (Exception ex) when (ex is
            IOException or OperationCanceledException or
            ObjectDisposedException or InvalidOperationException)
        {
            // Broken broker IPC is not a reason to keep a native mapping alive.
        }
        finally
        {
            _pipe.Dispose();
            _requests.Dispose();
            _stop.Dispose();
        }
    }
}
