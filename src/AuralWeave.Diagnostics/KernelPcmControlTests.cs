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
}
