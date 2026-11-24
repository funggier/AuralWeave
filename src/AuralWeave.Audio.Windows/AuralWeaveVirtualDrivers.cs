using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AuralWeave.Audio.Windows;

/// <summary>
/// Discovery is intentionally independent of the portable app folder.
/// We identify ONLY hardware IDs in our reserved vendor namespace, never
/// arbitrary devices whose names happen to contain "AuralWeave".
/// </summary>
public enum VirtualAudioEndpointRole { Input, Output }

public sealed record AuralWeaveVirtualDevice(
    string InstanceId, string Name, VirtualAudioEndpointRole Role,
    string HardwareId)
{
    public string RoleLabel => Role == VirtualAudioEndpointRole.Input
        ? "Virtual Input (recording / microphone)" : "Virtual Output (playback)";
}

public static class AuralWeaveVirtualDrivers
{
    // Matches the 16-ID-per-role prototype INF; no signed driver ships yet.
    public const string InputHardwarePrefix = "ROOT\\AURALWEAVE_VINPUT";
    public const string OutputHardwarePrefix = "ROOT\\AURALWEAVE_VOUTPUT";

    private const uint DigcfAllclasses = 0x00000004;
    private const uint SpdrpHardwareId = 0x00000001;
    private const uint SpdrpDeviceDesc = 0x00000000;
    private const uint SpdrpService = 0x00000004;
    private const string ExpectedServiceName = "AuralWeaveVirtualAudio";
    private static readonly Guid MediaClassGuid = new("4d36e96c-e325-11ce-bfc1-08002be10318");
    private const uint SpdrpFriendlyName = 0x0000000C;
    private const int ErrorNoMoreItems = 259;

    public static IReadOnlyList<AuralWeaveVirtualDevice> EnumerateInstalled()
    {
        if (!OperatingSystem.IsWindows()) return [];
        var infoSet = SetupDiGetClassDevs(
            IntPtr.Zero, null, IntPtr.Zero, DigcfAllclasses);
        if (infoSet == new IntPtr(-1))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "Unable to enumerate Windows Plug and Play devices.");
        try
        {
            var result = new List<AuralWeaveVirtualDevice>();
            for (uint index = 0; ; ++index)
            {
                var data = new SpDevinfoData
                {
                    Size = (uint)Marshal.SizeOf<SpDevinfoData>()
                };
                if (!SetupDiEnumDeviceInfo(infoSet, index, ref data))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ErrorNoMoreItems) break;
                    throw new Win32Exception(error, "PnP device enumeration failed.");
                }

                if (data.ClassGuid != MediaClassGuid ||
                    !ReadProperty(infoSet, ref data, SpdrpService).Equals(
                        ExpectedServiceName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var ids = ReadProperty(infoSet, ref data, SpdrpHardwareId)
                    .Split('\0', StringSplitOptions.RemoveEmptyEntries);
                var ownId = ids.FirstOrDefault(id =>
                    TryIdentifyRole(id, out _));
                if (ownId is null || !TryIdentifyRole(ownId, out var role))
                    continue;

                var instance = new StringBuilder(1024);
                if (!SetupDiGetDeviceInstanceId(infoSet, ref data,
                    instance, instance.Capacity, out _))
                    continue;

                var name = ReadProperty(infoSet, ref data, SpdrpFriendlyName);
                if (string.IsNullOrWhiteSpace(name))
                    name = ReadProperty(infoSet, ref data, SpdrpDeviceDesc);
                if (string.IsNullOrWhiteSpace(name))
                    name = role == VirtualAudioEndpointRole.Input
                        ? "AuralWeave Virtual Input" : "AuralWeave Virtual Output";
                result.Add(new AuralWeaveVirtualDevice(
                    instance.ToString(), name.TrimEnd('\0'), role, ownId));
            }
            return result.OrderBy(d => d.Role).ThenBy(d => d.Name,
                StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(infoSet);
        }
    }

    public static bool TryIdentifyRole(string hardwareId, out VirtualAudioEndpointRole role)
    {
        // Strict, boundary-aware namespace check. "AURALWEAVE" in the friendly
        // name is not evidence of ownership.
        if (IsOwnHardwareId(hardwareId, InputHardwarePrefix))
        {
            role = VirtualAudioEndpointRole.Input;
            return true;
        }
        if (IsOwnHardwareId(hardwareId, OutputHardwarePrefix))
        {
            role = VirtualAudioEndpointRole.Output;
            return true;
        }
        role = default;
        return false;
    }

    private static bool IsOwnHardwareId(string candidate, string prefix)
    {
        // Only the hardware IDs explicitly enumerated in the INF belong
        // to this driver. Never remove a generic/foreign PnP device because
        // its name or a misleading prefix happens to contain AuralWeave.
        if (candidate.Length != prefix.Length + 5 ||
            !candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            candidate[prefix.Length] != '_')
            return false;

        var number = 0;
        foreach (var c in candidate.AsSpan(prefix.Length + 1))
        {
            if (c is < '0' or > '9') return false;
            number = number * 10 + (c - '0');
        }
        return number is >= 1 and <= 16;
    }

    private static string ReadProperty(
        IntPtr infoSet, ref SpDevinfoData data, uint property)
    {
        var buffer = new byte[8192];
        if (!SetupDiGetDeviceRegistryProperty(infoSet, ref data, property,
            out _, buffer, (uint)buffer.Length, out _))
            return string.Empty;
        return Encoding.Unicode.GetString(buffer).TrimEnd('\0');
    }

    /// <summary>
    /// Removes only one verified AuralWeave devnode, not a shared driver-store
    /// package. Caller must confirm intent; Windows UAC is required.
    /// </summary>
    public static async Task<int> RequestDeviceRemovalAsync(
        AuralWeaveVirtualDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var current = EnumerateInstalled().Any(x =>
            x.InstanceId.Equals(device.InstanceId, StringComparison.OrdinalIgnoreCase) &&
            x.HardwareId.Equals(device.HardwareId, StringComparison.OrdinalIgnoreCase));
        if (!current)
            throw new InvalidOperationException(
                "Device identity could not be reverified. Refresh the list first.");

        var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var tool = Path.Combine(system32, "pnputil.exe");
        var start = new ProcessStartInfo
        {
            FileName = tool,
            Arguments = "/remove-device \"" + device.InstanceId.Replace("\"", "") + "\"",
            UseShellExecute = true,
            Verb = "runas"
        };
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Windows did not start PnPUtil.");
        await process.WaitForExitAsync().ConfigureAwait(false);
        return process.ExitCode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        public uint Size;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode,
        EntryPoint = "SetupDiGetClassDevsW", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        IntPtr classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet, uint memberIndex, ref SpDevinfoData deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode,
        EntryPoint = "SetupDiGetDeviceRegistryPropertyW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData,
        uint property, out uint propertyRegDataType, byte[] propertyBuffer,
        uint propertyBufferSize, out uint requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode,
        EntryPoint = "SetupDiGetDeviceInstanceIdW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInstanceId(
        IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData,
        StringBuilder deviceInstanceId, int deviceInstanceIdSize, out int requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
}
