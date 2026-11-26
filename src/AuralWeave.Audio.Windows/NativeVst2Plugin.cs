using System.Runtime.InteropServices;
using System.Text;

namespace AuralWeave.Audio.Windows;

/// <summary>An effect instance owned and disposed by the desktop UI thread.</summary>
public sealed class NativeVst2Plugin : IDisposable
{
    private const string Library = "AuralWeave.Native.dll";
    private const int ErrorCapacity = 512;
    private IntPtr _handle;

    public string Path { get; }
    public bool HasEditor => NativeHasEditor(Handle) != 0;

    internal IntPtr Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_handle == IntPtr.Zero, this);
            return _handle;
        }
    }

    public NativeVst2Plugin(string path, int sampleRate)
    {
        Path = System.IO.Path.GetFullPath(path);
        if (!Vst2PluginCatalog.IsVst2Plugin(Path))
            throw new InvalidDataException("Select a 64-bit VST2 effect DLL with a VSTPluginMain export.");

        var error = new StringBuilder(ErrorCapacity);
        _handle = NativeLoad(Path, sampleRate, error, (nuint)error.Capacity);
        if (_handle == IntPtr.Zero)
            throw Failure(error, "Could not load the VST2 effect.");
    }

    public void Configure(int sampleRate)
    {
        var error = new StringBuilder(ErrorCapacity);
        if (NativeConfigure(Handle, sampleRate, error, (nuint)error.Capacity) == 0)
            throw Failure(error, "Could not set the plugin sample rate.");
    }

    public void ShowEditor(IntPtr owner)
    {
        var error = new StringBuilder(ErrorCapacity);
        if (NativeShowEditor(Handle, owner, error, (nuint)error.Capacity) == 0)
            throw Failure(error, "Could not open the plugin editor.");
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        NativeClose(_handle);
        _handle = IntPtr.Zero;
    }

    private static InvalidOperationException Failure(StringBuilder error, string fallback) =>
        new(error.Length == 0 ? fallback : error.ToString());

    [DllImport(Library, EntryPoint = "aw_vst2_load", CallingConvention = CallingConvention.Cdecl,
        CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr NativeLoad(string path, int sampleRate, StringBuilder error, nuint size);

    [DllImport(Library, EntryPoint = "aw_vst2_close", CallingConvention = CallingConvention.Cdecl)]
    private static extern void NativeClose(IntPtr handle);

    [DllImport(Library, EntryPoint = "aw_vst2_configure", CallingConvention = CallingConvention.Cdecl,
        CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int NativeConfigure(IntPtr handle, int sampleRate, StringBuilder error, nuint size);

    [DllImport(Library, EntryPoint = "aw_vst2_show_editor", CallingConvention = CallingConvention.Cdecl,
        CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int NativeShowEditor(IntPtr handle, IntPtr owner, StringBuilder error, nuint size);

    [DllImport(Library, EntryPoint = "aw_vst2_has_editor", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NativeHasEditor(IntPtr handle);
}
