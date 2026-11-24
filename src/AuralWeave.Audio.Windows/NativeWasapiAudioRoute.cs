using System.Runtime.InteropServices;
using System.Text;
using AuralWeave.Core;

namespace AuralWeave.Audio.Windows;

/// <summary>
/// Optional P/Invoke bridge to AuralWeave.Native.dll. Uses individual device
/// shared-mode mix formats and bounded per-output linear resamplers.
/// </summary>
public sealed class NativeWasapiAudioRoute : IAudioRouteSession
{
    private IntPtr _handle;
    private readonly List<OutputRoute> _outputs = [];
    private AudioEffectInstance[] _effects = [];
    private bool _disposed;

    private const string Library = "AuralWeave.Native";

    public NativeWasapiAudioRoute()
    {
        _handle = NativeCreate();
        if (_handle == IntPtr.Zero)
            throw new InvalidOperationException("Could not allocate native audio route.");
    }

    public static bool Available
    {
        get
        {
            // Explicit path: do not resolve a DLL from an unrelated working directory.
            var path = Path.Combine(AppContext.BaseDirectory, "AuralWeave.Native.dll");
            if (!File.Exists(path)) return false;
            try
            {
                if (!NativeLibrary.TryLoad(path, out var module)) return false;
                NativeLibrary.Free(module);
                return true;
            }
            catch { return false; }
        }
    }

    public bool IsRunning => _handle != IntPtr.Zero && NativeState(_handle) == 2;
    public long CapturedFrames => _handle == IntPtr.Zero ? 0 : (long)NativeCapturedFrames(_handle);
    public float InputPeak => _handle == IntPtr.Zero ? 0 : NativeInputPeak(_handle);
    public long DroppedRenderFrames => _handle == IntPtr.Zero ? 0 : (long)NativeDroppedFrames(_handle);

    public string? LastError
    {
        get
        {
            if (_handle == IntPtr.Zero) return null;
            var text = new StringBuilder(2048);
            NativeLastError(_handle, text, (nuint)text.Capacity);
            return text.Length == 0 ? null : text.ToString();
        }
    }

    public void Start(string sourceId, IReadOnlyList<OutputRoute> outputs)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentNullException.ThrowIfNull(outputs);
        AudioRouteRules.ValidateOutputs(outputs);
        if (outputs.Count == 0) throw new ArgumentException("Choose at least one output.");
        if (IsRunning) throw new InvalidOperationException("Route already running.");
        var ids = outputs.Select(x => x.EndpointId).ToArray();
        var gains = outputs.Select(x => x.Gain).ToArray();
        var muted = outputs.Select(x => x.Muted ? 1 : 0).ToArray();
        if (NativeStart(_handle, sourceId, ids, gains, muted, outputs.Count) != 1)
            throw new InvalidOperationException(LastError ?? "Native WASAPI route failed to start.");
        _outputs.Clear();
        _outputs.AddRange(outputs);
        ApplyEffects();
    }

    public void ReplaceEffects(IEnumerable<AudioEffectInstance> effects)
    {
        ThrowIfDisposed();
        var updated = effects.ToArray();
        AudioRouteRules.ValidateEffects(updated);
        _effects = updated;
        if (IsRunning) ApplyEffects();
    }

    private void ApplyEffects()
    {
        var gains = _effects.Select(x => x.Gain).ToArray();
        var enabled = _effects.Select(x => x.Enabled ? 1 : 0).ToArray();
        if (NativeSetEffects(_handle, gains, enabled, gains.Length) != 1)
            throw new InvalidOperationException("Native backend rejected effect settings.");
    }

    public void SetOutputLevel(Guid outputId, float gain, bool muted)
    {
        ThrowIfDisposed();
        var index = _outputs.FindIndex(x => x.Id == outputId);
        if (index < 0) throw new ArgumentException("Selected output was not found.");
        if (NativeSetOutput(_handle, index, gain, muted ? 1 : 0) != 1)
            throw new ArgumentException("Native backend rejected output gain.");
        _outputs[index] = _outputs[index] with { Gain = gain, Muted = muted };
    }

    public IReadOnlyList<OutputTelemetry> GetOutputTelemetry()
    {
        if (_handle == IntPtr.Zero) return [];
        var message = LastError;
        // Total dropped frames are exposed separately (not falsely assigned to each sink).
        return _outputs.Select(x => new OutputTelemetry(x.Id, double.NaN, 0, message)).ToArray();
    }

    public void Stop()
    {
        if (_handle == IntPtr.Zero) return;
        NativeStop(_handle);
        _outputs.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle == IntPtr.Zero) return;
        NativeDestroy(_handle); // AwDestroy stops and joins worker.
        _handle = IntPtr.Zero;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    [DllImport(Library, EntryPoint = "aw_create", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr NativeCreate();

    [DllImport(Library, EntryPoint = "aw_destroy", CallingConvention = CallingConvention.Cdecl)]
    private static extern void NativeDestroy(IntPtr handle);

    [DllImport(Library, EntryPoint = "aw_start", CallingConvention = CallingConvention.Cdecl,
        CharSet = CharSet.Unicode)]
    private static extern int NativeStart(IntPtr handle, [MarshalAs(UnmanagedType.LPWStr)] string sourceId,
        [In, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] endpointIds,
        [In] float[] gains, [In] int[] muted, int count);

    [DllImport(Library, EntryPoint = "aw_stop", CallingConvention = CallingConvention.Cdecl)]
    private static extern void NativeStop(IntPtr handle);

    [DllImport(Library, EntryPoint = "aw_state", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NativeState(IntPtr handle);

    [DllImport(Library, EntryPoint = "aw_captured_frames", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong NativeCapturedFrames(IntPtr handle);

    [DllImport(Library, EntryPoint = "aw_dropped_render_frames", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong NativeDroppedFrames(IntPtr handle);

    [DllImport(Library, EntryPoint = "aw_input_peak", CallingConvention = CallingConvention.Cdecl)]
    private static extern float NativeInputPeak(IntPtr handle);

    [DllImport(Library, EntryPoint = "aw_set_effects", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NativeSetEffects(IntPtr handle, [In] float[] gains, [In] int[] enabled, int count);

    [DllImport(Library, EntryPoint = "aw_set_output", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NativeSetOutput(IntPtr handle, int index, float gain, int muted);

    [DllImport(Library, EntryPoint = "aw_last_error", CallingConvention = CallingConvention.Cdecl,
        CharSet = CharSet.Unicode)]
    private static extern void NativeLastError(IntPtr handle, [Out] StringBuilder buffer, nuint capacity);
}
