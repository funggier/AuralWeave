using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AuralWeave.Audio.Windows;

/// <summary>
/// Retrieves the actual client process ID bound to a connected Windows
/// Named Pipe handle. This is stronger than accepting a PID sent in text.
/// It is used ONLY to cross-check advisory PCM mapping ownership in the
/// non-elevated, same-user MOCK broker, not to grant privileged access.
/// </summary>
internal static class LocalBrokerPeerIdentity
{
    [DllImport("kernel32.dll", EntryPoint = "GetNamedPipeClientProcessId",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(
        SafePipeHandle pipe, out uint clientProcessId);

    internal static uint GetClientProcessId(NamedPipeServerStream pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows named pipes only.");
        if (!pipe.IsConnected || !GetNamedPipeClientProcessId(
                pipe.SafePipeHandle, out var clientId) || clientId == 0)
            throw new IOException("Cannot verify mock broker pipe client's process ID.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        return clientId;
    }
}
