using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AuralWeave.Desktop;

/// <summary>
/// WindowStyle=None / WindowChrome can maximize over the taskbar. In particular,
/// a TOP-docked Windows taskbar completely hides our 39px custom caption bar.
/// Intercept WM_GETMINMAXINFO so maximized bounds match the active monitor's
/// rcWork instead of the whole monitor. Coordinates are monitor-relative to
/// support multi-monitor layouts, including negative desktop coordinates.
/// </summary>
internal sealed class WindowWorkAreaMaximizer
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 0x00000002;

    private HwndSource? _source;

    public WindowWorkAreaMaximizer(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.SourceInitialized += (_, _) =>
        {
            _source = (HwndSource?)PresentationSource.FromVisual(window);
            _source?.AddHook(WindowProc);
        };
        window.Closed += (_, _) =>
        {
            _source?.RemoveHook(WindowProc);
            _source = null;
        };
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam,
                              IntPtr lParam, ref bool handled)
    {
        if (msg != WmGetMinMaxInfo || lParam == IntPtr.Zero)
            return IntPtr.Zero;

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return IntPtr.Zero;

        var info = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };
        if (!GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;

        var work = info.WorkArea;
        var monitorBounds = info.MonitorArea;
        var maxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        maxInfo.MaxPosition = new NativePoint(
            work.Left - monitorBounds.Left,
            work.Top - monitorBounds.Top);
        maxInfo.MaxSize = new NativePoint(
            work.Right - work.Left,
            work.Bottom - work.Top);
        // Keep native maximize and track behavior; only override bounds.
        Marshal.StructureToPtr(maxInfo, lParam, fDeleteOld: false);
        handled = true;
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
        public NativePoint(int x, int y) { X = x; Y = y; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
