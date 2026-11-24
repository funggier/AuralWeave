using System.Buffers;
using AuralWeave.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AuralWeave.Audio.Windows;

/// <summary>
/// One shared-mode WASAPI input, ordered DSP rack, and multiple independent WASAPI outputs.
/// This prototype uses bounded capture-format buffers and Media Foundation conversion for
/// each physical sink. Independent device clock drift management is still future work.
/// </summary>
public sealed class WasapiAudioRoute : IDisposable
{
    private readonly object _callbackGate = new();
    private readonly AudioBlockProcessor _processor = new();
    private readonly List<OutputSink> _outputs = [];
    private WasapiCapture? _capture;
    private MMDevice? _sourceDevice;
    private MMDeviceEnumerator? _enumerator;
    private PcmSampleEncoding? _encoding;
    private volatile bool _accepting;
    private long _capturedFrames;
    private float _inputPeak;
    private string? _lastError;

    public bool IsRunning => _accepting;
    public long CapturedFrames => Interlocked.Read(ref _capturedFrames);
    public float InputPeak => Volatile.Read(ref _inputPeak);
    public string? LastError => Volatile.Read(ref _lastError);

    public void ReplaceEffects(IEnumerable<AudioEffectInstance> effects) =>
        _processor.ReplaceEffects(effects);

    public void Start(string sourceId, IReadOnlyList<OutputRoute> outputs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentNullException.ThrowIfNull(outputs);
        if (_capture is not null) throw new InvalidOperationException("Route is already initialized.");
        if (outputs.Count == 0) throw new ArgumentException("Select at least one output.");
        AudioRouteRules.ValidateOutputs(outputs);

        // Prevent immediate feedback from one loopback endpoint into itself.
        if (sourceId.StartsWith("loopback:", StringComparison.Ordinal) &&
            outputs.Any(x => x.EndpointId == sourceId["loopback:".Length..]))
            throw new ArgumentException("A loopback source cannot route to the same playback endpoint.");

        try
        {
            _enumerator = new MMDeviceEnumerator();
            var loopback = sourceId.StartsWith("loopback:", StringComparison.Ordinal);
            var captureId = loopback
                ? sourceId["loopback:".Length..]
                : sourceId.StartsWith("capture:", StringComparison.Ordinal)
                    ? sourceId["capture:".Length..]
                    : throw new ArgumentException("Unsupported source endpoint kind.");
            _sourceDevice = _enumerator.GetDevice(captureId);
            _capture = loopback
                ? new WasapiLoopbackCapture(_sourceDevice)
                : new WasapiCapture(_sourceDevice);

            var sourceFormat = _capture.WaveFormat;
            _encoding = GetEncoding(sourceFormat);
            if (_encoding is null && _processor.HasProcessing)
                throw new NotSupportedException("Gain processing requires PCM16 or Float32 capture.");

            foreach (var selection in outputs)
            {
                var device = _enumerator.GetDevice(selection.EndpointId);
                try
                {
                    var sink = new OutputSink(device, sourceFormat, selection);
                    _outputs.Add(sink);
                }
                catch
                {
                    device.Dispose();
                    throw;
                }
            }

            _capture.DataAvailable += OnAudioAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            Volatile.Write(ref _inputPeak, 0f);
            Interlocked.Exchange(ref _capturedFrames, 0);
            Volatile.Write(ref _lastError, null);
            foreach (var sink in _outputs) sink.Play();
            _accepting = true;
            _capture.StartRecording();
        }
        catch
        {
            Stop();
            throw;
        }
    }

    public void SetOutputLevel(Guid outputId, float gain, bool muted)
    {
        if (!float.IsFinite(gain) || gain < 0 || gain > 2)
            throw new ArgumentOutOfRangeException(nameof(gain));
        lock (_callbackGate)
        {
            var sink = _outputs.FirstOrDefault(x => x.Id == outputId)
                ?? throw new ArgumentException("Output is not connected to the current route.");
            sink.Gain = gain;
            sink.Muted = muted;
        }
    }

    public IReadOnlyList<OutputTelemetry> GetOutputTelemetry()
    {
        lock (_callbackGate)
            return _outputs.Select(x => new OutputTelemetry(
                x.Id, x.BufferedMilliseconds, x.LastPeak, x.LastError)).ToArray();
    }

    private void OnAudioAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        if (!_accepting || eventArgs.BytesRecorded <= 0) return;
        lock (_callbackGate)
        {
            if (!_accepting) return;
            var data = eventArgs.Buffer;
            var length = eventArgs.BytesRecorded;
            byte[]? sharedCopy = null;
            try
            {
                if (_encoding is { } format)
                    Volatile.Write(ref _inputPeak, AudioBlockProcessor.Peak(
                        data.AsSpan(0, length), format));

                Interlocked.Add(ref _capturedFrames, length / _capture!.WaveFormat.BlockAlign);

                if (_processor.HasProcessing)
                {
                    sharedCopy = ArrayPool<byte>.Shared.Rent(length);
                    Buffer.BlockCopy(data, 0, sharedCopy, 0, length);
                    _processor.Process(sharedCopy.AsSpan(0, length), _encoding!.Value);
                    data = sharedCopy;
                }

                foreach (var sink in _outputs)
                {
                    if (sink.LastError is not null) continue;
                    byte[]? sinkCopy = null;
                    try
                    {
                        var gain = sink.Gain;
                        var muted = sink.Muted;
                        if (muted || gain != 1f)
                        {
                            sinkCopy = ArrayPool<byte>.Shared.Rent(length);
                            if (muted) Array.Clear(sinkCopy, 0, length);
                            else
                            {
                                if (_encoding is null)
                                    throw new NotSupportedException("Output gain is unsupported for this sample format.");
                                Buffer.BlockCopy(data, 0, sinkCopy, 0, length);
                                // Output gain is applied as an independent final stage per sink.
                                AudioBlockProcessor.ApplyGain(sinkCopy.AsSpan(0, length), _encoding.Value, gain);
                            }
                            sink.Write(sinkCopy, length);
                        }
                        else sink.Write(data, length);
                    }
                    catch (Exception ex) { sink.LastError = ex.Message; }
                    finally { if (sinkCopy is not null) ArrayPool<byte>.Shared.Return(sinkCopy); }
                }
            }
            catch (Exception ex) { Volatile.Write(ref _lastError, ex.Message); }
            finally { if (sharedCopy is not null) ArrayPool<byte>.Shared.Return(sharedCopy); }
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        _accepting = false;
        if (args.Exception is not null)
            Volatile.Write(ref _lastError, args.Exception.Message);
    }

    public void Stop()
    {
        _accepting = false;
        var capture = _capture;
        if (capture is not null)
        {
            try { capture.StopRecording(); } catch { /* Device could be disconnected. */ }
        }
        lock (_callbackGate)
        {
            if (capture is not null)
            {
                capture.DataAvailable -= OnAudioAvailable;
                capture.RecordingStopped -= OnRecordingStopped;
                capture.Dispose();
                _capture = null;
            }
            foreach (var sink in _outputs)
            {
                try { sink.Dispose(); } catch { /* Preserve cleanup of other sinks. */ }
            }
            _outputs.Clear();
            _sourceDevice?.Dispose();
            _sourceDevice = null;
            _enumerator?.Dispose();
            _enumerator = null;
            Volatile.Write(ref _inputPeak, 0f);
        }
    }

    public void Dispose() => Stop();

    private static PcmSampleEncoding? GetEncoding(WaveFormat format)
    {
        var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat;
        var isPcm = format.Encoding == WaveFormatEncoding.Pcm;
        if (format is WaveFormatExtensible extensible)
        {
            isFloat = extensible.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71");
            isPcm = extensible.SubFormat == new Guid("00000001-0000-0010-8000-00aa00389b71");
        }
        if (isFloat && format.BitsPerSample == 32) return PcmSampleEncoding.Float32;
        if (isPcm && format.BitsPerSample == 16) return PcmSampleEncoding.Int16;
        return null;
    }

    private sealed class OutputSink : IDisposable
    {
        private readonly MMDevice _device;
        private readonly BufferedWaveProvider _buffer;
        private readonly MediaFoundationResampler _converter;
        private readonly WasapiOut _player;
        private readonly PcmSampleEncoding? _sampleEncoding;

        public Guid Id { get; }
        public float Gain { get; set; }
        public bool Muted { get; set; }
        public float LastPeak { get; private set; }
        public string? LastError { get; set; }
        public double BufferedMilliseconds => _buffer.BufferedDuration.TotalMilliseconds;

        public OutputSink(MMDevice device, WaveFormat sourceFormat, OutputRoute route)
        {
            _device = device;
            _sampleEncoding = GetEncoding(sourceFormat);
            Id = route.Id;
            Gain = route.Gain;
            Muted = route.Muted;
            _buffer = new BufferedWaveProvider(sourceFormat)
            {
                BufferDuration = TimeSpan.FromMilliseconds(450),
                ReadFully = true,
                DiscardOnBufferOverflow = true
            };
            // MF performs sample rate, channel and PCM conversion to the actual
            // endpoint mix format (bypass affects DSP, not required format conversion).
            _converter = new MediaFoundationResampler(_buffer, device.AudioClient.MixFormat)
            {
                ResamplerQuality = 40
            };
            _player = new WasapiOut(device, AudioClientShareMode.Shared, true, 90);
            _player.Init(_converter);
            _player.PlaybackStopped += (_, args) =>
            {
                if (args.Exception is not null) LastError = args.Exception.Message;
            };
        }

        public void Play() => _player.Play();
        public void Write(byte[] data, int count)
        {
            if (_sampleEncoding is { } encoding)
                LastPeak = AudioBlockProcessor.Peak(data.AsSpan(0, count), encoding);
            _buffer.AddSamples(data, 0, count);
        }

        public void Dispose()
        {
            _player.Stop();
            _player.Dispose();
            _converter.Dispose();
            _device.Dispose();
        }
    }
}

public sealed record OutputTelemetry(
    Guid OutputId,
    double BufferedMilliseconds,
    float Peak,
    string? Error);

internal static class AudioGain
{
    public static void Apply(Span<byte> audio, PcmSampleEncoding encoding, float gain)
    {
        // Share the exact same saturating gain algorithm used by effect instances.
        var processor = new AudioBlockProcessor();
        processor.ReplaceEffects([new AudioEffectInstance(Guid.NewGuid(), AudioRouteRules.GainEffect, true, gain)]);
        processor.Process(audio, encoding);
    }
}
