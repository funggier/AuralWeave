namespace AuralWeave.Audio.Windows;

/// <summary>
/// Opt-in, synchronous bridge stage for a FUTURE trusted elevated broker:
/// - consumes the mixed Virtual Input bus (Float32) and writes PCM S32 to
///   the Windows virtual microphone kernel queue
/// - reads virtual-speaker PCM S16 from the kernel queue and publishes
///   Float32 to the Virtual Output bus
///
/// This is a reference/data-path component, not an automatic service.
/// No unsigned driver is installed or opened by constructing this object.
/// The caller owns the PCM mappings, supplies a trusted kernel client and
/// schedules PumpOnce at the desired frame cadence (e.g. 256 at 48kHz).
/// </summary>
public sealed class VirtualKernelAudioPump
{
    private readonly VirtualKernelPcmClient _driver;
    private readonly NativeVirtualAudioBridge _micReader;
    private readonly NativeVirtualAudioBridge _speakerWriter;
    private readonly float[] _input;
    private readonly int _blockFrames;

    public VirtualKernelAudioPump(VirtualKernelPcmClient driver,
        NativeVirtualAudioBridge micReader,
        NativeVirtualAudioBridge speakerWriter, int blockFrames = 256)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(micReader);
        ArgumentNullException.ThrowIfNull(speakerWriter);
        if (blockFrames is < 1 or > VirtualKernelPcmProtocol.MaxFrames)
            throw new ArgumentOutOfRangeException(nameof(blockFrames));
        if (micReader.Role != VirtualKernelPcmProtocol.InputRole ||
            speakerWriter.Role != VirtualKernelPcmProtocol.OutputRole ||
            micReader.Slot != 1 || speakerWriter.Slot != 1 ||
            micReader.Channels != 2 || speakerWriter.Channels != 2 ||
            micReader.GetStats().SampleRate != 48000 ||
            speakerWriter.GetStats().SampleRate != 48000)
            throw new InvalidOperationException(
                "Expected single-slot, 48-kHz stereo Input reader and Output writer.");
        _driver = driver;
        _micReader = micReader;
        _speakerWriter = speakerWriter;
        _blockFrames = blockFrames;
        _input = new float[blockFrames * 2];
    }

    internal VirtualKernelPcmClient Driver => _driver;
    public int BlockFrames => _blockFrames;

    public ulong MicrophoneFramesAccepted { get; private set; }
    public ulong MicrophoneFramesDropped { get; private set; }
    public ulong SpeakerFramesDelivered { get; private set; }
    public ulong SpeakerFramesDropped { get; private set; }

    /// <summary>
    /// Never sleeps, installs a device, mutates PnP or spins waiting for
    /// audio. Returns the number of microphone+speaker frames moved.
    /// Caller must handle recoverable driver/service disconnections.
    /// </summary>
    public int PumpOnce()
    {
        // Pin the transfer to the RUN lifetime observed before reading a block.
        var input = _driver.Query(VirtualKernelPcmProtocol.InputRole);
        var micFrames = _micReader.Read(_input);
        var microphoneAccepted = 0u;
        if (micFrames > 0)
        {
            if (input.SampleRate != 0)
            {
                microphoneAccepted = _driver.WriteMicrophone(
                    _input.AsSpan(0, checked((int)micFrames * 2)), input.Generation);
            }
            MicrophoneFramesAccepted += microphoneAccepted;
            MicrophoneFramesDropped += micFrames - microphoneAccepted;
        }

        var output = _driver.Query(VirtualKernelPcmProtocol.OutputRole);
        float[] speaker = output.SampleRate == 0
            ? [] : _driver.ReadSpeaker(_blockFrames, output.Generation);
        var speakerFrames = (uint)(speaker.Length / 2);
        var speakerDelivered = 0u;
        if (speakerFrames > 0)
        {
            speakerDelivered = _speakerWriter.Write(speaker, speakerFrames);
            SpeakerFramesDelivered += speakerDelivered;
            SpeakerFramesDropped += speakerFrames - speakerDelivered;
        }
        return checked((int)(microphoneAccepted + speakerDelivered));
    }
}
