using System.Buffers.Binary;
using AuralWeave.Audio.Windows;

internal static class KernelPcmControlTests
{
    private sealed class SimulatedDriver : IVirtualKernelPcmExchange
    {
        public int[] CapturedMicrophoneSamples { get; private set; } = [];
        public int Writes { get; private set; }
        public bool TamperRequestId { get; set; }
        public bool TamperSampleRate { get; set; }
        public bool TamperChannels { get; set; }
        public bool NoActiveStream { get; set; }
        public bool FailSpeakerRead { get; set; }

        public byte[] Exchange(byte[] request, int maxResponse)
        {
            var req = VirtualKernelPcmProtocol.Decode(request);
            if (req.SampleRate != 48000 || req.Channels != 2)
                throw new InvalidDataException("Unexpected prototype PCM format.");
            static uint Bits(uint role) =>
                role == VirtualKernelPcmProtocol.InputRole ? 32u : 16u;
            var expectedFormat = req.Role == VirtualKernelPcmProtocol.InputRole
                ? VirtualKernelPcmProtocol.Signed32Format
                : VirtualKernelPcmProtocol.Signed16Format;
            if (req.Format != expectedFormat || req.Bits != Bits(req.Role))
                throw new InvalidDataException("Wrong kernel PCM sample encoding.");
            byte[] data = [];
            uint frames = 0;

            switch (req.Opcode)
            {
                case VirtualKernelPcmProtocol.InputWrite:
                    if (req.Role != VirtualKernelPcmProtocol.InputRole ||
                        req.PayloadBytes != req.Frames * 8 ||
                        req.Frames > VirtualKernelPcmProtocol.MaxFrames)
                        throw new InvalidDataException("Invalid microphone WRITE.");
                    CapturedMicrophoneSamples = new int[req.Frames * 2];
                    for (var i = 0; i < CapturedMicrophoneSamples.Length; i++)
                        CapturedMicrophoneSamples[i] = BinaryPrimitives.ReadInt32LittleEndian(
                            request.AsSpan(VirtualKernelPcmProtocol.HeaderSize + i * 4));
                    Writes++;
                    frames = req.Frames;
                    break;
                case VirtualKernelPcmProtocol.OutputRead:
                    if (FailSpeakerRead)
                        throw new IOException("Simulated kernel device disconnected.");
                    if (req.Role != VirtualKernelPcmProtocol.OutputRole ||
                        req.PayloadBytes != 0 || req.Frames < 1 ||
                        req.Frames > VirtualKernelPcmProtocol.MaxFrames)
                        throw new InvalidDataException("Invalid speaker READ.");
                    frames = Math.Min(req.Frames, 2);
                    data = new byte[frames * 4];
                    var signal = new short[] {16384, -16384, 8192, -8192};
                    for (var i = 0; i < data.Length / 2; i++)
                        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i * 2), signal[i]);
                    break;
                case VirtualKernelPcmProtocol.Status:
                    break;
                default:
                    throw new InvalidDataException("Unsupported mocked operation.");
            }
            var reply = VirtualKernelPcmProtocol.Encode(
                new VirtualKernelPcmProtocol.Header(req.Opcode, req.Role,
                    req.Format, req.SampleRate, req.Channels, req.Bits,
                    frames, (uint)data.Length,
                    TamperRequestId ? req.RequestId + 1 : req.RequestId), data);
            if (req.Opcode == VirtualKernelPcmProtocol.Status)
            {
                // A live kernel queue can have >2048 frames even though each
                // read/write IOCTL transfers a maximum of 2048 at once.
                BinaryPrimitives.WriteUInt32LittleEndian(reply.AsSpan(40), 8192);
            }
            if (req.Opcode == VirtualKernelPcmProtocol.Status && NoActiveStream)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(reply.AsSpan(28), 0);
                BinaryPrimitives.WriteUInt32LittleEndian(reply.AsSpan(32), 0);
            }
            if (TamperSampleRate)
                BinaryPrimitives.WriteUInt32LittleEndian(reply.AsSpan(28), 44100);
            if (TamperChannels)
                BinaryPrimitives.WriteUInt32LittleEndian(reply.AsSpan(32), 1);
            if (reply.Length > maxResponse)
                throw new InvalidDataException("Mock exceeded METHOD_BUFFERED response size.");
            return reply;
        }
    }

    public static void Run()
    {
        static void Check(bool success, string reason)
        {
            if (!success) throw new InvalidOperationException(reason);
            Console.WriteLine("PASS: " + reason);
        }

        var fake = new SimulatedDriver();
        var client = new VirtualKernelPcmClient(fake);
        var header = client.Query(VirtualKernelPcmProtocol.InputRole);
        Check(header.Frames == 8192 && header.Format == VirtualKernelPcmProtocol.Signed32Format,
            "kernel status safely accepts queued counts >2048");

        var written = client.WriteMicrophone([1f, -1f, float.NaN, 0.25f]);
        Check(written == 2 && fake.Writes == 1 &&
            fake.CapturedMicrophoneSamples.Length == 4 &&
            fake.CapturedMicrophoneSamples[0] == int.MaxValue &&
            fake.CapturedMicrophoneSamples[1] == int.MinValue &&
            fake.CapturedMicrophoneSamples[2] == 0 &&
            Math.Abs(fake.CapturedMicrophoneSamples[3] - int.MaxValue / 4) <= 1,
            "Float32->signed PCM32 saturates, handles NaN, preserves stereo order");

        var output = client.ReadSpeaker(256);
        Check(output.Length == 4 &&
            Math.Abs(output[0] - 0.5f) < 0.0001f &&
            Math.Abs(output[1] + 0.5f) < 0.0001f &&
            Math.Abs(output[2] - 0.25f) < 0.0001f &&
            Math.Abs(output[3] + 0.25f) < 0.0001f,
            "signed PCM16 speaker output becomes Float32 without clipping or channel swap");

        try
        {
            client.ReadSpeaker(2049);
            throw new InvalidOperationException("Oversized speaker request was accepted");
        }
        catch (ArgumentOutOfRangeException)
        {
            Console.WriteLine("PASS: speaker request above 2048 frames rejected");
        }

        try
        {
            client.WriteMicrophone(new float[4098]);
            throw new InvalidOperationException("Oversized microphone request was accepted");
        }
        catch (ArgumentException)
        {
            Console.WriteLine("PASS: microphone request above 2048 stereo frames rejected");
        }

        fake.TamperRequestId = true;
        try
        {
            client.ReadSpeaker(1);
            throw new InvalidOperationException("Tampered kernel request ID was accepted");
        }
        catch (InvalidDataException)
        {
            Console.WriteLine("PASS: mismatched kernel request ID rejected");
        }
        fake.TamperRequestId = false;

        fake.TamperSampleRate = true;
        try
        {
            client.ReadSpeaker(1);
            throw new InvalidOperationException("Incompatible kernel sample rate was accepted.");
        }
        catch (InvalidDataException)
        {
            Console.WriteLine("PASS: mismatched kernel response sample rate rejected");
        }
        fake.TamperSampleRate = false;

        fake.TamperChannels = true;
        try
        {
            client.ReadSpeaker(1);
            throw new InvalidOperationException("Incompatible kernel channel count was accepted.");
        }
        catch (InvalidDataException)
        {
            Console.WriteLine("PASS: mismatched kernel response channel geometry rejected");
        }
        fake.TamperChannels = false;

        var normal = VirtualKernelPcmProtocol.Encode(
            new VirtualKernelPcmProtocol.Header(
                VirtualKernelPcmProtocol.Status,
                VirtualKernelPcmProtocol.InputRole,
                VirtualKernelPcmProtocol.Signed32Format, 48000, 2, 32, 0, 0, 3),
            []);
        var corrupt = (byte[])normal.Clone();
        corrupt[0] ^= 0x80;
        try
        {
            VirtualKernelPcmProtocol.Decode(corrupt);
            throw new InvalidOperationException("Corrupt kernel reply magic was accepted.");
        }
        catch (InvalidDataException) { Console.WriteLine("PASS: invalid PCM packet magic rejected"); }
        corrupt = (byte[])normal.Clone();
        corrupt[48] = 1; // reserved response flags
        try
        {
            VirtualKernelPcmProtocol.Decode(corrupt);
            throw new InvalidOperationException("Response flags were not validated.");
        }
        catch (InvalidDataException) { Console.WriteLine("PASS: unknown kernel reply flags rejected"); }
        corrupt = (byte[])normal.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(corrupt.AsSpan(16), 99);
        try
        {
            VirtualKernelPcmProtocol.Decode(corrupt);
            throw new InvalidOperationException("Unknown kernel role was accepted.");
        }
        catch (InvalidDataException) { Console.WriteLine("PASS: unknown kernel PCM role rejected"); }
        corrupt = (byte[])normal.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(corrupt.AsSpan(40), 4096);
        BinaryPrimitives.WriteUInt32LittleEndian(corrupt.AsSpan(12),
            VirtualKernelPcmProtocol.OutputRead);
        BinaryPrimitives.WriteUInt32LittleEndian(corrupt.AsSpan(16),
            VirtualKernelPcmProtocol.OutputRole);
        BinaryPrimitives.WriteUInt32LittleEndian(corrupt.AsSpan(24),
            VirtualKernelPcmProtocol.Signed16Format);
        BinaryPrimitives.WriteUInt32LittleEndian(corrupt.AsSpan(36), 16);
        try
        {
            VirtualKernelPcmProtocol.Decode(corrupt);
            throw new InvalidOperationException("Oversized audio transfer frame count was accepted.");
        }
        catch (InvalidDataException) { Console.WriteLine("PASS: oversized non-status PCM frame count rejected"); }

        // Full data-path stage: a Float32 post-mix virtual-microphone
        // mapping is consumed, converted to PCM32, and the mocked kernel
        // speaker PCM16 is emitted back into a separate Float32 mapping.
        // All channels are user mode: NO actual kernel driver is touched.
        using (var micOwner = NativeVirtualAudioBridge.Create(1, 1, 48000, 2, 512))
        using (var speakerOwner = NativeVirtualAudioBridge.Create(2, 1, 48000, 2, 512))
        using (var micConsumer = NativeVirtualAudioBridge.Attach(micOwner.Name, 1, 1))
        using (var speakerProducer = NativeVirtualAudioBridge.Attach(speakerOwner.Name, 2, 1))
        {
            var mock = new SimulatedDriver();
            var pump = new VirtualKernelAudioPump(new VirtualKernelPcmClient(mock),
                micConsumer, speakerProducer, 256);
            Check(micOwner.Write([0.1f, 0.2f, 0.3f, 0.4f]) == 2,
                "Native Shared PCM virtual mic sender accepted sample block");
            var moved = pump.PumpOnce();
            var speakerSamples = new float[4];
            var read = speakerOwner.Read(speakerSamples);
            Check(moved == 4 && read == 2 &&
                  pump.MicrophoneFramesAccepted == 2 &&
                  pump.SpeakerFramesDelivered == 2 &&
                  mock.CapturedMicrophoneSamples.Length == 4 &&
                  Math.Abs(mock.CapturedMicrophoneSamples[0] / (float)int.MaxValue - 0.1f) < 0.0001f &&
                  Math.Abs(speakerSamples[0] - 0.5f) < 0.0001f &&
                  Math.Abs(speakerSamples[1] + 0.5f) < 0.0001f,
                "user-mode PCM bus -> kernel protocol mock -> second PCM bus data path");
        }
        Console.WriteLine("KERNEL_PCM_USER_MODE_MOCK_BRIDGE=PASS (no driver installed)");
    }

    /// <summary>
    /// A Windows 10 user-mode/Native DLL regression only. Exercises the
    /// future broker's explicit start/stop/format gate and failure path.
    /// Never opens or installs a kernel audio driver.
    /// </summary>
    public static async Task RunWorkerAsync()
    {
        using var inputOwner = NativeVirtualAudioBridge.Create(1, 1, 48000, 2, 512);
        using var outputOwner = NativeVirtualAudioBridge.Create(2, 1, 48000, 2, 512);
        using var inputReader = NativeVirtualAudioBridge.Attach(inputOwner.Name, 1, 1);
        using var outputWriter = NativeVirtualAudioBridge.Attach(outputOwner.Name, 2, 1);
        var fake = new SimulatedDriver();
        var driver = new VirtualKernelPcmClient(fake);
        var pump = new VirtualKernelAudioPump(driver, inputReader, outputWriter, 64);
        await using (var worker = new VirtualKernelAudioWorker(driver, pump))
        {
            worker.Start();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while (worker.Cycles < 3 && !timeout.IsCancellationRequested)
                await Task.Delay(10);
            if (worker.Cycles < 3 || worker.ActiveCycles == 0 ||
                worker.LastFailure is not null)
                throw new InvalidOperationException(
                    "Explicit kernel PCM worker did not advance mock data without error.");

            await worker.StopAsync();
            var stoppedCycles = worker.Cycles;
            await Task.Delay(60);
            if (worker.IsRunning || worker.Cycles != stoppedCycles)
                throw new InvalidOperationException(
                    "Virtual PCM worker continued pumping after StopAsync.");
            await worker.StopAsync(); // Idempotent.
            try
            {
                worker.Start();
                throw new InvalidOperationException(
                    "Single-use PCM worker restarted without new validation.");
            }
            catch (InvalidOperationException ex) when (
                ex.Message.Contains("single-use", StringComparison.Ordinal))
            {
                Console.WriteLine("PASS: worker stop is idempotent and restart is explicit.");
            }
        }
        Console.WriteLine("PASS: paced virtual PCM mock worker starts and stops without a busy retry loop.");

        // Both WaveRT endpoints must be active BEFORE the first audio tick.
        var detached = new SimulatedDriver { NoActiveStream = true };
        var detachedDriver = new VirtualKernelPcmClient(detached);
        var detachedPump = new VirtualKernelAudioPump(
            detachedDriver, inputReader, outputWriter, 64);
        await using (var worker = new VirtualKernelAudioWorker(detachedDriver, detachedPump))
        {
            try
            {
                worker.Start();
                throw new InvalidOperationException(
                    "Worker started when no kernel WaveRT stream was attached.");
            }
            catch (InvalidOperationException ex) when (
                ex.Message.Contains("absent", StringComparison.Ordinal))
            {
                Console.WriteLine("PASS: detached kernel stream prevents worker startup.");
            }
        }

        // A lost driver must stop the loop, preserving the original cause.
        var failed = new SimulatedDriver { FailSpeakerRead = true };
        var failedDriver = new VirtualKernelPcmClient(failed);
        var failedPump = new VirtualKernelAudioPump(
            failedDriver, inputReader, outputWriter, 64);
        await using (var worker = new VirtualKernelAudioWorker(failedDriver, failedPump))
        {
            worker.Start();
            try
            {
                await worker.Completion.WaitAsync(TimeSpan.FromSeconds(3));
                throw new InvalidOperationException(
                    "Disconnected simulated driver kept the worker running.");
            }
            catch (IOException ex) when (
                ex.Message.Contains("disconnected", StringComparison.Ordinal))
            {
                if (worker.IsRunning || worker.LastFailure is not IOException)
                    throw new InvalidOperationException(
                        "Worker failed to publish terminal device loss.");
                Console.WriteLine("PASS: disconnected kernel mock fails closed with visible error.");
            }
        }

        Console.WriteLine("VIRTUAL_KERNEL_PCM_WORKER_LIFECYCLE=PASS (user-mode only)");

        // Two independent Portable-style PCM sources produce microphone
        // audio for a single kernel-facing channel. Speaker output is
        // distributed to two independent listeners, with no stolen frames.
        var sharedMock = new SimulatedDriver();
        var sharedDriver = new VirtualKernelPcmClient(sharedMock);
        using (var coordinator = new VirtualKernelAudioCoordinator(sharedDriver, 64, 512))
        using (var sourceA = NativeVirtualAudioBridge.Create(1, 1, 48000, 2, 512))
        using (var sourceB = NativeVirtualAudioBridge.Create(1, 1, 48000, 2, 512))
        using (var earA = NativeVirtualAudioBridge.Create(2, 1, 48000, 2, 512))
        using (var earB = NativeVirtualAudioBridge.Create(2, 1, 48000, 2, 512))
        {
            coordinator.AddMicrophoneSource(sourceA.Name);
            coordinator.AddMicrophoneSource(sourceB.Name);
            coordinator.AddSpeakerListener(earA.Name);
            coordinator.AddSpeakerListener(earB.Name);

            if (sourceA.Write([0.25f, 0.25f, 0.25f, 0.25f]) != 2 ||
                sourceB.Write([0.40f, 0.40f, 0.40f, 0.40f]) != 2 ||
                coordinator.PumpOnce() != 4)
                throw new InvalidOperationException(
                    "Multi-client Virtual Input mixer did not transfer a full block.");

            var samplesA = new float[4];
            var samplesB = new float[4];
            if (earA.Read(samplesA) != 2 || earB.Read(samplesB) != 2 ||
                sharedMock.CapturedMicrophoneSamples.Length != 4 ||
                Math.Abs(sharedMock.CapturedMicrophoneSamples[0] /
                         (float)int.MaxValue - 0.65f) > 0.0001f ||
                samplesA.Where((value, index) =>
                    Math.Abs(value - samplesB[index]) > 0.00001f).Any() ||
                Math.Abs(samplesA[0] - 0.5f) > 0.0001f ||
                Math.Abs(samplesA[1] + 0.5f) > 0.0001f ||
                coordinator.MicrophoneSources != 2 ||
                coordinator.SpeakerListeners != 2 ||
                coordinator.DroppedSpeakerFrames != 0)
                throw new InvalidOperationException(
                    "Single kernel PCM channel failed multi-source mix or independent fanout.");
            Console.WriteLine("PASS: two microphone sources mix to 0.65 and two speaker listeners hear identical PCM.");

            if (!coordinator.RemoveMicrophoneSource(sourceA.Name) ||
                sourceB.Write([0.20f, 0.20f, 0.20f, 0.20f]) != 2 ||
                coordinator.PumpOnce() != 4 ||
                Math.Abs(sharedMock.CapturedMicrophoneSamples[0] /
                         (float)int.MaxValue - 0.20f) > 0.0001f)
                throw new InvalidOperationException(
                    "Single remaining Portable source did not survive peer removal.");
            Console.WriteLine("PASS: shared virtual microphone continues when a source disconnects.");

            // The same bounded worker now schedules the complete coordinator
            // pipeline; no Windows kernel driver or new Windows service.
            await using var scheduled = new VirtualKernelAudioWorker(
                sharedDriver, coordinator);
            scheduled.Start();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while (scheduled.Cycles < 2 && !deadline.IsCancellationRequested)
                await Task.Delay(10);
            if (scheduled.Cycles < 2 || scheduled.LastFailure is not null)
                throw new InvalidOperationException(
                    "Scheduled multi-client coordinator did not advance.");
            await scheduled.StopAsync();
            Console.WriteLine("PASS: scheduled coordinator remains explicitly stoppable.");
        }
        Console.WriteLine("VIRTUAL_KERNEL_MULTI_CLIENT_COORDINATOR=PASS (user-mode mock only)");
    }
}
