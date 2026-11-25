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

        public static async Task<Connection> OpenAsync()
        {
            var pipe = new NamedPipeClientStream(".", LocalVirtualAudioBroker.PipeName,
                PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(8000);
                return new Connection(pipe);
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
            if (await clientB.PingAsync() != "OK PONG" ||
                !(await listener.QueryStatusAsync()).Contains(
                    "input=2 output=1", StringComparison.Ordinal))
                throw new InvalidOperationException("Managed broker client cannot query health/status.");
            await WaitForStatusAsync(inspector, "input=2 output=1");

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
