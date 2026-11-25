using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using AuralWeave.Audio.Windows;

/// <summary>
/// True cross-process Named Pipe control-plane and Native Float32 PCM smoke.
/// A separate non-elevated Broker.exe process contains no Kernel driver I/O.
/// </summary>
internal static class LocalBrokerIntegrationTests
{
    private sealed class Connection : IAsyncDisposable
    {
        private readonly NamedPipeClientStream _pipe;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;

        private Connection(NamedPipeClientStream pipe)
        {
            _pipe = pipe;
            _reader = new StreamReader(pipe, Encoding.ASCII, false, 1024, leaveOpen: true);
            _writer = new StreamWriter(pipe, Encoding.ASCII, 1024, leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\n"
            };
        }

        public Guid BrokerInstanceId { get; private set; }

        public static async Task<Connection> OpenAsync()
        {
            var pipe = new NamedPipeClientStream(".", LocalVirtualAudioBroker.PipeName,
                PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(8000);
                var client = new Connection(pipe);
                var hello = await client.SendAsync("HELLO 2");
                const string prefix = "OK HELLO 2 instance=";
                if (!hello.StartsWith(prefix, StringComparison.Ordinal) ||
                    !Guid.TryParseExact(hello[prefix.Length..], "N", out var instance))
                    throw new InvalidOperationException("Broker sent malformed protocol greeting.");
                client.BrokerInstanceId = instance;
                return client;
            }
            catch
            {
                pipe.Dispose();
                throw;
            }
        }

        public async Task<string> SendAsync(string command)
        {
            await _writer.WriteLineAsync(command);
            return await _reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5))
                ?? throw new EndOfStreamException("Broker disconnected without reply.");
        }

        private bool _disposed;

        public ValueTask DisposeAsync()
        {
            if (_disposed)
                return ValueTask.CompletedTask;
            _disposed = true;
            // QUIT or a network-style disconnect may already have closed
            // the server end before StreamWriter flushes during Dispose.
            try { _writer.Dispose(); }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            _reader.Dispose();
            _pipe.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private static string BrokerDll()
    {
        var fallback = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "AuralWeave.Broker", "bin",
            "Release", "net8.0-windows", "AuralWeave.Broker.dll"));
        var path = Environment.GetEnvironmentVariable("AW_BROKER_DLL") ?? fallback;
        if (!File.Exists(path))
            throw new FileNotFoundException(
                "Build the separate AuralWeave.Broker project before running IPC tests.", path);
        return path;
    }

    private static Process LaunchBroker()
    {
        var portableExe = Environment.GetEnvironmentVariable("AW_BROKER_EXE");
        var isPortable = !string.IsNullOrWhiteSpace(portableExe);
        if (isPortable && !File.Exists(portableExe))
            throw new FileNotFoundException("Requested Portable mock broker was not found.",
                portableExe);
        var info = new ProcessStartInfo(isPortable ? portableExe! : "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            UseShellExecute = false
        };
        if (!isPortable)
            info.ArgumentList.Add(BrokerDll());
        info.ArgumentList.Add("--mock");
        return Process.Start(info) ??
            throw new InvalidOperationException("Unable to start the mock broker process.");
    }

    private static async Task StopProcessAsync(Process child)
    {
        if (!child.HasExited)
            child.Kill(entireProcessTree: true);
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
    }

    private static async Task WaitForStatusAsync(Connection monitor, string wanted)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(4))
        {
            var result = await monitor.SendAsync("STATUS");
            if (result.Contains(wanted, StringComparison.Ordinal))
                return;
            await Task.Delay(20);
        }
        throw new InvalidOperationException("Broker status did not reach: " + wanted);
    }

    private static async Task CheckFramesAsync(NativeVirtualAudioBridge listener, float expected)
    {
        var buffer = new float[4];
        for (var i = 0; i < 80; i++)
        {
            if (listener.Read(buffer) == 2)
            {
                if (Math.Abs(buffer[0] - expected) > 0.0001f ||
                    Math.Abs(buffer[1] + expected) > 0.0001f)
                    throw new InvalidOperationException(
                        $"Broker mixed incorrectly; wanted {expected}, got {buffer[0]}.");
                return;
            }
            await Task.Delay(20);
        }
        throw new InvalidOperationException("Broker pipe registration produced no PCM loopback.");
    }

    /// <summary>
    /// Kill a real broker process without graceful QUIT; make sure old control
    /// pipes fail, restart with a different process identity, and create fresh
    /// FIFO rings rather than replaying audio written for the dead process.
    /// No actual kernel driver or Windows virtual audio endpoint is involved.
    /// </summary>
    public static async Task RunRestartAsync()
    {
        using var first = LaunchBroker();
        Process? second = null;
        try
        {
            if (await first.StandardOutput.ReadLineAsync()
                .WaitAsync(TimeSpan.FromSeconds(8))
                != "AURALWEAVE_MOCK_BROKER_STARTING")
                throw new InvalidOperationException("Initial mock broker failed to start.");

            // A peer that skips HELLO must not access even the status API.
            using (var wrongPipe = new NamedPipeClientStream(".",
                LocalVirtualAudioBroker.PipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous))
            {
                await wrongPipe.ConnectAsync(8000);
                var ping = Encoding.ASCII.GetBytes("PING\n");
                await wrongPipe.WriteAsync(ping);
                using var wrongReader = new StreamReader(wrongPipe, Encoding.ASCII,
                    false, 1024, leaveOpen: true);
                if (await wrongReader.ReadLineAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5)) != "ERR PROTOCOL_VERSION")
                    throw new InvalidOperationException(
                        "Broker accepted an unversioned control connection.");
            }

            await using var firstMic = await LocalBrokerEndpointSession.OpenAsync(1);
            await using var firstSpeaker = await LocalBrokerEndpointSession.OpenAsync(2);
            var previousId = firstMic.BrokerInstanceId;
            var previousMicMapping = firstMic.Pcm.Name;
            var previousSpeakerMapping = firstSpeaker.Pcm.Name;
            if (previousId == Guid.Empty || firstSpeaker.BrokerInstanceId != previousId)
                throw new InvalidOperationException("Invalid first process instance ID.");

            // Reopening without an actual restart must fail and release its
            // own temporary native mapping and pipe.
            try
            {
                await using var unexpected = await
                    LocalBrokerEndpointSession.OpenAfterRestartAsync(1, previousId);
                throw new InvalidOperationException("Same-process reconnect was accepted.");
            }
            catch (InvalidOperationException e) when (
                e.Message.Contains("has not changed", StringComparison.Ordinal))
            {
                Console.WriteLine("PASS: reject false reconnect within the same broker lifetime.");
            }

            if (firstMic.Pcm.Write([0.15f, -0.15f, 0.15f, -0.15f]) != 2)
                throw new InvalidOperationException("Initial mock microphone cannot write.");
            await CheckFramesAsync(firstSpeaker.Pcm, 0.15f);
            Console.WriteLine("PASS: first broker instance transports registered PCM.");

            await StopProcessAsync(first); // force hard-kill with live clients
            try
            {
                await firstMic.Control.PingAsync();
                throw new InvalidOperationException(
                    "Dead broker unexpectedly answered an old control pipe.");
            }
            catch (IOException) { }
            if (firstMic.Control.LastFailure is null)
                throw new InvalidOperationException(
                    "Dead broker did not publish failed control connectivity.");

            // This sample belongs to the dead broker and MUST NOT be replayed
            // by the fresh-ring reconnect helper.
            if (firstMic.Pcm.Write([0.95f, -0.95f, 0.95f, -0.95f]) != 2)
                throw new InvalidOperationException("Cannot queue stale old-session PCM.");
            await firstMic.DisposeAsync();
            await firstSpeaker.DisposeAsync();

            second = LaunchBroker();
            if (await second.StandardOutput.ReadLineAsync()
                .WaitAsync(TimeSpan.FromSeconds(8))
                != "AURALWEAVE_MOCK_BROKER_STARTING")
                throw new InvalidOperationException("Replacement mock broker failed to start.");
            await using var inspector = await Connection.OpenAsync();
            if (inspector.BrokerInstanceId == previousId ||
                !(await inspector.SendAsync("STATUS")).Contains(
                    "input=0 output=0", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Replacement broker reused an old instance ID or retained clients.");

            await using var nextMic =
                await LocalBrokerEndpointSession.OpenAfterRestartAsync(1, previousId);
            await using var nextSpeaker =
                await LocalBrokerEndpointSession.OpenAfterRestartAsync(2, previousId);
            if (nextMic.Pcm.Name == previousMicMapping ||
                nextSpeaker.Pcm.Name == previousSpeakerMapping ||
                nextMic.BrokerInstanceId != nextSpeaker.BrokerInstanceId ||
                nextMic.BrokerInstanceId != inspector.BrokerInstanceId)
                throw new InvalidOperationException(
                    "New broker did not receive brand-new PCM ownership sessions.");

            await WaitForStatusAsync(inspector, "input=1 output=1");
            var empty = new float[4];
            if (nextSpeaker.Pcm.Read(empty) != 0)
                throw new InvalidOperationException(
                    "New output FIFO leaked old broker PCM before new audio.");

            if (nextMic.Pcm.Write([0.32f, -0.32f, 0.32f, -0.32f]) != 2)
                throw new InvalidOperationException("New mock microphone cannot write.");
            await CheckFramesAsync(nextSpeaker.Pcm, 0.32f);
            Console.WriteLine(
                "PASS: hard-killed broker can restart with new epoch, new rings, no stale audio replay.");

            await StopProcessAsync(second);
            Console.WriteLine(
                "LOCAL_MOCK_BROKER_CRASH_RESTART_RECOVERY=PASS (explicit, bounded, no kernel)");
        }
        finally
        {
            if (second is not null)
            {
                await StopProcessAsync(second);
                second.Dispose();
            }
            await StopProcessAsync(first);
        }
    }

    public static async Task RunAsync()
    {
        using var broker = LaunchBroker();
        try
        {
            var startLine = await broker.StandardOutput.ReadLineAsync()
                .WaitAsync(TimeSpan.FromSeconds(8));
            if (startLine != "AURALWEAVE_MOCK_BROKER_STARTING")
                throw new InvalidOperationException(
                    "Mock broker failed to initialize: " +
                    await broker.StandardError.ReadToEndAsync());
            await using var inspector = await Connection.OpenAsync();
            if (inspector.BrokerInstanceId == Guid.Empty)
                throw new InvalidOperationException("Broker greeting has no instance ID.");
            if (await inspector.SendAsync("PING") != "OK PONG" ||
                !((await inspector.SendAsync("STATUS")).Contains(
                    "input=0 output=0", StringComparison.Ordinal)))
                throw new InvalidOperationException("Broker did not respond to health/status.");

            if (await inspector.SendAsync("REGISTER INPUT C:\\not-a-virtual-map")
                != "ERR INVALID_MAPPING")
                throw new InvalidOperationException("Broker accepted an arbitrary path as PCM.");
            await using (var oversized = await Connection.OpenAsync())
            {
                try
                {
                    await oversized.SendAsync(new string('X', 260));
                    throw new InvalidOperationException(
                        "Oversized IPC request was accepted.");
                }
                catch (IOException) { }
            }
            if (await inspector.SendAsync("PING") != "OK PONG")
                throw new InvalidOperationException(
                    "Malformed IPC peer stopped healthy broker control clients.");
            Console.WriteLine("PASS: oversized IPC command isolated without terminating broker.");

            using var micA = NativeVirtualAudioBridge.Create(1, 1, 48000, 2, 512);
            using var micB = NativeVirtualAudioBridge.Create(1, 1, 48000, 2, 512);
            using var ears = NativeVirtualAudioBridge.Create(2, 1, 48000, 2, 512);
            await using var clientA = await Connection.OpenAsync();
            if (await clientA.SendAsync("REGISTER INPUT " + micA.Name)
                    != "OK REGISTERED INPUT")
                throw new InvalidOperationException("Broker rejected first source.");
            // Use the reusable Studio-facing managed client API for the
            // other two sessions, including its automatic heartbeat and QUIT.
            await using var clientB = await LocalBrokerControlClient.ConnectAsync(micB);
            await using var listener = await LocalBrokerControlClient.ConnectAsync(ears);
            if (clientB.BrokerInstanceId != inspector.BrokerInstanceId ||
                listener.BrokerInstanceId != inspector.BrokerInstanceId ||
                await clientB.PingAsync() != "OK PONG" ||
                !(await listener.QueryStatusAsync()).Contains(
                    "input=2 output=1", StringComparison.Ordinal))
                throw new InvalidOperationException("Managed broker client cannot query health/status.");
            await WaitForStatusAsync(inspector, "input=2 output=1");

            var beforeHeartbeat = await clientB.GetStatusAsync();
            if (beforeHeartbeat.BrokerInstanceId != inspector.BrokerInstanceId ||
                beforeHeartbeat.MicrophoneSources != 2 ||
                beforeHeartbeat.SpeakerListeners != 1)
                throw new InvalidOperationException(
                    "Typed mock broker status has an incorrect lifetime or membership.");
            // No explicit PING from the managed clients during this interval.
            // The automatically scheduled 4-second heartbeat must prevent
            // their 15-second idle control-session timeout.
            await Task.Delay(TimeSpan.FromMilliseconds(5500));
            var afterHeartbeat = await clientB.GetStatusAsync();
            if (afterHeartbeat.ReceivedPings <= beforeHeartbeat.ReceivedPings ||
                afterHeartbeat.MicrophoneSources != 2 ||
                afterHeartbeat.SpeakerListeners != 1)
                throw new InvalidOperationException(
                    "Managed client heartbeat did not keep its registration active.");
            Console.WriteLine("PASS: typed status and automatic heartbeat preserve idle PCM registrations.");

            // A different IPC client cannot claim a registered mapping.
            await using (var duplicate = await Connection.OpenAsync())
            {
                if (await duplicate.SendAsync("REGISTER INPUT " + micA.Name)
                        != "ERR ATTACH_REJECTED")
                    throw new InvalidOperationException("Duplicate PCM ownership registration accepted.");
            }

            if (micA.Write([0.25f, -0.25f, 0.25f, -0.25f]) != 2 ||
                micB.Write([0.40f, -0.40f, 0.40f, -0.40f]) != 2)
                throw new InvalidOperationException("Failed to populate independent source rings.");
            await CheckFramesAsync(ears, 0.65f);
            Console.WriteLine("PASS: separate mock broker mixes two independent PCM contributors via Named Pipe registration.");

            if (await clientA.SendAsync("QUIT") != "OK BYE")
                throw new InvalidOperationException("Broker did not acknowledge explicit disconnect.");
            await WaitForStatusAsync(inspector, "input=1 output=1");
            if (micB.Write([0.20f, -0.20f, 0.20f, -0.20f]) != 2)
                throw new InvalidOperationException("Surviving source cannot write after peer QUIT.");
            await CheckFramesAsync(ears, 0.20f);
            Console.WriteLine("PASS: single client QUIT unregisters only its own mapping and other PCM continues.");

            // Abrupt control connection loss removes a mapping even when
            // the owning PCM Shared Memory still exists.
            await listener.DisposeAsync();
            await WaitForStatusAsync(inspector, "input=1 output=0");
            Console.WriteLine("PASS: lost control pipe removes its listener without a kernel restart.");

            // Do not let a second process bind the same broker pipe while
            // the first holds the per-session singleton lease.
            using var duplicateBroker = LaunchBroker();
            if (!duplicateBroker.WaitForExit(8000) || duplicateBroker.ExitCode == 0)
            {
                await StopProcessAsync(duplicateBroker);
                throw new InvalidOperationException("A duplicate mock broker was allowed to start.");
            }
            Console.WriteLine("PASS: second broker process refused for same Windows session.");

            if (await inspector.SendAsync("QUIT") != "OK BYE")
                throw new InvalidOperationException("Broker did not acknowledge monitor logout.");
            await StopProcessAsync(broker);
            Console.WriteLine("LOCAL_VIRTUAL_BROKER_PIPE_AND_PCM_MOCK=PASS (no service, no kernel)");
        }
        finally
        {
            await StopProcessAsync(broker);
        }
    }
}
