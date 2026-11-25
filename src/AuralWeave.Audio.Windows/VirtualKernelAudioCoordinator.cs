namespace AuralWeave.Audio.Windows;

/// <summary>
/// DEVELOPMENT-ONLY, explicitly pumped user-mode coordinator for multiple
/// Portable Studio clients sharing one logical Virtual Input + Output.
///
/// Microphone: contributors -> mixed user-mode bus -> one PCM32 IOCTL writer.
/// Speaker: one PCM16 IOCTL reader -> user-mode bus -> isolated listeners.
///
/// This is not a privileged broker, discoverable service, kernel driver or
/// product audio endpoint. The caller provides a test/mock kernel client;
/// no driver is loaded, signed, installed or opened by this class.
/// </summary>
public sealed class VirtualKernelAudioCoordinator : IDisposable
{
    private readonly List<IDisposable> _owned = [];
    private readonly NativeVirtualAudioBridge _inputToKernel;
    private readonly NativeVirtualAudioBridge _outputFromKernel;
    private readonly SharedVirtualAudioBus _inputBus;
    private readonly SharedVirtualAudioBus _outputBus;
    private readonly VirtualKernelAudioPump _kernelPump;
    private bool _disposed;

    public VirtualKernelAudioCoordinator(VirtualKernelPcmClient driver,
        int blockFrames = 256, uint capacityFrames = 2048)
    {
        ArgumentNullException.ThrowIfNull(driver);
        if (blockFrames is < 32 or > VirtualKernelPcmProtocol.MaxFrames)
            throw new ArgumentOutOfRangeException(nameof(blockFrames));
        if (capacityFrames < (uint)blockFrames || capacityFrames > 65536)
            throw new ArgumentOutOfRangeException(nameof(capacityFrames));

        try
        {
            // These two mappings are the only bridges touching the future
            // kernel pump. Each remains a single-producer/single-consumer ring.
            _inputToKernel = Own(NativeVirtualAudioBridge.Create(
                VirtualKernelPcmProtocol.InputRole, 1, 48000, 2, capacityFrames));
            _outputFromKernel = Own(NativeVirtualAudioBridge.Create(
                VirtualKernelPcmProtocol.OutputRole, 1, 48000, 2, capacityFrames));

            var kernelInputReader = Own(NativeVirtualAudioBridge.Attach(
                _inputToKernel.Name, VirtualKernelPcmProtocol.InputRole, 1));
            var kernelOutputWriter = Own(NativeVirtualAudioBridge.Attach(
                _outputFromKernel.Name, VirtualKernelPcmProtocol.OutputRole, 1));

            _inputBus = Own(new SharedVirtualAudioBus(
                VirtualKernelPcmProtocol.InputRole, 48000, 2, blockFrames));
            _outputBus = Own(new SharedVirtualAudioBus(
                VirtualKernelPcmProtocol.OutputRole, 48000, 2, blockFrames));

            _inputBus.AddListener(_inputToKernel.Name);
            _outputBus.AddContributor(_outputFromKernel.Name);
            _kernelPump = new VirtualKernelAudioPump(
                driver, kernelInputReader, kernelOutputWriter, blockFrames);
        }
        catch
        {
            ReleaseOwned();
            throw;
        }
    }

    public int MicrophoneSources => _inputBus.Contributors;
    public int SpeakerListeners => _outputBus.Listeners;
    public ulong MixedMicrophoneFrames => _inputBus.FramesMixed;
    public ulong DeliveredSpeakerFrames => _outputBus.FramesDelivered;
    public ulong DroppedSpeakerFrames => _outputBus.FramesDropped;
    public VirtualKernelAudioPump KernelPump => _kernelPump;

    public void AddMicrophoneSource(string mappingName)
    {
        ThrowIfDisposed();
        _inputBus.AddContributor(mappingName);
    }

    public bool RemoveMicrophoneSource(string mappingName)
    {
        ThrowIfDisposed();
        return _inputBus.RemoveContributor(mappingName);
    }

    public void AddSpeakerListener(string mappingName)
    {
        ThrowIfDisposed();
        _outputBus.AddListener(mappingName);
    }

    public bool RemoveSpeakerListener(string mappingName)
    {
        ThrowIfDisposed();
        return _outputBus.RemoveListener(mappingName);
    }

    /// <summary>
    /// One bounded, synchronous stage. Caller schedules it on ONE thread:
    /// mix first, then kernel, then independent speaker fanout. Callers may
    /// register/unregister peers only between ticks; not concurrently.
    /// If IOCTL fails, the exception propagates and caller must stop.
    /// </summary>
    public int PumpOnce()
    {
        ThrowIfDisposed();
        _inputBus.Pump();
        var frames = _kernelPump.PumpOnce();
        _outputBus.Pump();
        return frames;
    }

    private T Own<T>(T disposable) where T : IDisposable
    {
        _owned.Add(disposable);
        return disposable;
    }

    private void ReleaseOwned()
    {
        for (var index = _owned.Count - 1; index >= 0; index--)
            _owned[index].Dispose();
        _owned.Clear();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseOwned();
        GC.SuppressFinalize(this);
    }
}
