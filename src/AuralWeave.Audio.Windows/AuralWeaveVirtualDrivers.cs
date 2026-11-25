using System.ComponentModel;
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
    string HardwareId, bool IsDriverBound = true)
{
    public string RoleLabel => Role == VirtualAudioEndpointRole.Input
        ? "Virtual Input (recording / microphone)" : "Virtual Output (playback)";
    // Only for migration/cleanup. Legacy slots 0002..0016 are no longer
    // eligible for installation but must remain discoverable for removal.
    public bool IsLegacy => !AuralWeaveVirtualDrivers.TryIdentifyRole(HardwareId, out _);
}

public static class AuralWeaveVirtualDrivers
{
    // One installable PnP ID per role on Windows 10/11. No signed package yet.
    public const int MaxVirtualInputs = 1;
    public const int MaxVirtualOutputs = 1;
    public const string InputHardwarePrefix = "ROOT\\AURALWEAVE_VINPUT";
    public const string OutputHardwarePrefix = "ROOT\\AURALWEAVE_VOUTPUT";
    public const string InputHardwareId = "ROOT\\AURALWEAVE_VINPUT_0001";
    public const string OutputHardwareId = "ROOT\\AURALWEAVE_VOUTPUT_0001";

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

                // Enumerate exact reserved PnP IDs even if the driver service
                // is missing or the devnode was only partly registered. Such
                // a node STILL blocks creation of a duplicate root device.
                // Do not mistake a missing service for an absent device.
                var ids = ReadProperty(infoSet, ref data, SpdrpHardwareId)
                    .Split('\0', StringSplitOptions.RemoveEmptyEntries);
                var ownId = ids.FirstOrDefault(id =>
                    TryIdentifyRole(id, out _) || TryIdentifyLegacyRole(id, out _));
                if (ownId is null)
                    continue;
                if (!TryIdentifyRole(ownId, out var role) &&
                    !TryIdentifyLegacyRole(ownId, out role))
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
                var bound = data.ClassGuid == MediaClassGuid &&
                    ReadProperty(infoSet, ref data, SpdrpService).Equals(
                        ExpectedServiceName, StringComparison.OrdinalIgnoreCase);
                result.Add(new AuralWeaveVirtualDevice(
                    instance.ToString(), name.TrimEnd('\0'), role, ownId, bound));
            }
            return result.OrderBy(d => d.Role).ThenBy(d => d.Name,
                StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(infoSet);
        }
    }

    public static bool TryIdentifyRole(string? hardwareId, out VirtualAudioEndpointRole role)
    {
        // Only these two IDs are current. An unrecognized ID must not count
        // as a second supported virtual endpoint (or a normal device).
        if (string.Equals(hardwareId, InputHardwareId, StringComparison.OrdinalIgnoreCase))
        {
            role = VirtualAudioEndpointRole.Input;
            return true;
        }
        if (string.Equals(hardwareId, OutputHardwareId, StringComparison.OrdinalIgnoreCase))
        {
            role = VirtualAudioEndpointRole.Output;
            return true;
        }
        role = default;
        return false;
    }

    public static bool TryIdentifyLegacyRole(string? hardwareId,
                                             out VirtualAudioEndpointRole role)
    {
        // Previous UNSIGNED prototypes advertised 0002..0016. They are now
        // deprecated, but if installed on a test machine they must be shown
        // as legacy devices so a user can explicitly uninstall them.
        if (IsLegacyHardwareId(hardwareId, InputHardwarePrefix))
        {
            role = VirtualAudioEndpointRole.Input;
            return true;
        }
        if (IsLegacyHardwareId(hardwareId, OutputHardwarePrefix))
        {
            role = VirtualAudioEndpointRole.Output;
            return true;
        }
        role = default;
        return false;
    }

    private static bool IsLegacyHardwareId(string? candidate, string prefix)
    {
        if (candidate is null || candidate.Length != prefix.Length + 5 ||
            !candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            candidate[prefix.Length] != '_')
            return false;
        var number = 0;
        foreach (var c in candidate.AsSpan(prefix.Length + 1))
        {
            if (c is < '0' or > '9') return false;
            number = number * 10 + c - '0';
        }
        return number is >= 2 and <= 16;
    }

    // Future signed installer MUST check the installed inventory before
    // creating ANY device node (and serialize concurrent install attempts).
    // Existing legacy IDs block a new install until explicitly removed.
    public static bool MayInstall(
        IReadOnlyList<AuralWeaveVirtualDevice> installed,
        VirtualAudioEndpointRole role)
    {
        ArgumentNullException.ThrowIfNull(installed);
        return !installed.Any(device => device.Role == role);
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
