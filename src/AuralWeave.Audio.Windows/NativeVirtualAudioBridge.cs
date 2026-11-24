using System.Runtime.InteropServices;
using System.Text;

namespace AuralWeave.Audio.Windows;

/// <summary>
/// Separate user-mode IPC transport for development of future AuralWeave
/// virtual audio endpoints. Does NOT install or communicate with a kernel driver.
/// Fixed SPSC contract: exactly one writer and one reader per mapping.
/// </summary>
public sealed class NativeVirtualAudioBridge : IDisposable
{
    private const string Library = "AuralWeave.Native";
    private IntPtr _handle;
    private readonly uint _channels;
    private bool _disposed;

    private NativeVirtualAudioBridge(IntPtr handle, string name, uint role, uint slot, uint channels)
    {
        _handle = handle;
        Name = name;
        Role = role;
        Slot = slot;
        _channels = channels;
    }

    public string Name { get; }
    public uint Role { get; }
    public uint Slot { get; }
    public uint Channels => _channels;

    public static NativeVirtualAudioBridge Create(uint role, uint slot,
        uint sampleRate = 48000, uint channels = 2, uint capacityFrames = 4096)
    {
        var name = new StringBuilder(160);
        var handle = NativeCreate(role, slot, sampleRate, channels, capacityFrames,
            name, (uint)name.Capacity);
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("Native PCM bridge create failed: " + GetLastError());
        return new NativeVirtualAudioBridge(handle, name.ToString(), role, slot, channels);
    }

    public static NativeVirtualAudioBridge Attach(string name, uint role, uint slot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var handle = NativeAttach(name, role, slot);
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException("Native PCM bridge attach failed: " + GetLastError());
        if (NativeGetStats(handle, out var stats) == 0)
        {
            NativeClose(handle);
            throw new InvalidOperationException("Native PCM bridge metadata unavailable.");
        }
        return new NativeVirtualAudioBridge(handle, name, role, slot, stats.Channels);
    }

    public uint Write(float[] samples)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Length % _channels != 0)
            throw new ArgumentException("PCM sample array must contain whole frames.", nameof(samples));
        return NativeWrite(_handle, samples, (uint)(samples.Length / _channels));
    }

    public uint Read(float[] samples, bool padWithSilence = false)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Length % _channels != 0)
            throw new ArgumentException("PCM sample array must contain whole frames.", nameof(samples));
        return NativeRead(_handle, samples, (uint)(samples.Length / _channels),
            padWithSilence ? 1 : 0);
    }

    public VirtualBridgeStats GetStats()
    {
        ThrowIfDisposed();
        if (NativeGetStats(_handle, out var stats) != 1)
            throw new InvalidOperationException("PCM bridge statistics unavailable.");
        return stats;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle != IntPtr.Zero)
        {
            NativeClose(_handle);
            _handle = IntPtr.Zero;
        }
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static string GetLastError()
    {
        var buffer = new StringBuilder(512);
        NativeLastError(buffer, (uint)buffer.Capacity);
        return buffer.ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct VirtualBridgeStats
    {
        public readonly ulong FramesWritten;
        public readonly ulong FramesRead;
        public readonly ulong OverflowFrames;
        public readonly ulong UnderflowFrames;
        public readonly uint QueuedFrames;
        public readonly uint ClosedByOwner;
        public readonly uint SampleRate;
        public readonly uint Channels;
        public readonly uint CapacityFrames;
    }

    [DllImport(Library, EntryPoint = "aw_bridge_create", CallingConvention = CallingConvention.Cdecl,
        CharSet = CharSet.Unicode)]
    private static extern IntPtr NativeCreate(uint role, uint slot, uint rate, uint channels,
        uint capacityFrames, [Out] StringBuilder name, uint nameCapacity);

    [DllImport(Library, EntryPoint = "aw_bridge_attach", CallingConvention = CallingConvention.Cdecl,
        CharSet = CharSet.Unicode)]
    private static extern IntPtr NativeAttach([MarshalAs(UnmanagedType.LPWStr)] string name,
        uint role, uint slot);

    [DllImport(Library, EntryPoint = "aw_bridge_close", CallingConvention = CallingConvention.Cdecl)]
    private static extern void NativeClose(IntPtr handle);

    [DllImport(Library, EntryPoint = "aw_bridge_write", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint NativeWrite(IntPtr handle, [In] float[] samples, uint frames);

    [DllImport(Library, EntryPoint = "aw_bridge_read", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint NativeRead(IntPtr handle, [Out] float[] samples,
        uint frames, int padWithSilence);

    [DllImport(Library, EntryPoint = "aw_bridge_get_stats", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NativeGetStats(IntPtr handle, out VirtualBridgeStats stats);

    [DllImport(Library, EntryPoint = "aw_bridge_last_error", CallingConvention = CallingConvention.Cdecl,
        CharSet = CharSet.Unicode)]
    private static extern uint NativeLastError([Out] StringBuilder message, uint capacity);
}
