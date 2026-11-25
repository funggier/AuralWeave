// AuralWeave Virtual Driver Setup — Windows 10 production helper SOURCE.
// Original AuralWeave MIT code. Not signed or shipped with current Portable.
//
// --check: read-only, never requires elevation or creates a device.
// --install-input / --install-output: fail closed unless the complete driver
// package and exact pinned publisher certificate pass production verification.
// No unsigned driver, test signing, Secure Boot change or kernel driver
// deployment is possible with the current empty certificate pin.
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using AuralWeave.Audio.Windows;

namespace AuralWeave.DriverSetup;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1)
            {
                ShowUsage();
                return 64;
            }

            if (args[0].Equals("--check", StringComparison.OrdinalIgnoreCase))
            {
                var current = AuralWeaveVirtualDrivers.EnumerateInstalled();
                foreach (var checkedRole in new[] {
                    VirtualAudioEndpointRole.Input, VirtualAudioEndpointRole.Output })
                {
                    var readiness = VirtualDriverInstallation.CheckBundle(
                        current, checkedRole, AppContext.BaseDirectory);
                    Console.WriteLine(readiness.Description);
                }
                // A read-only status command must not create any PnP nodes.
                return 0;
            }

            var role = args[0].ToLowerInvariant() switch
            {
                "--install-input" => VirtualAudioEndpointRole.Input,
                "--install-output" => VirtualAudioEndpointRole.Output,
                _ => throw new ArgumentException(
                    "Only --check, --install-input and --install-output are supported.")
            };

            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19045))
                throw new PlatformNotSupportedException(
                    "Production Virtual Audio installation requires Windows 10 22H2 or later.");

            if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
                throw new PlatformNotSupportedException(
                    "The AuralWeave Virtual Audio driver package is x64-only.");

            if (!IsRunningAsAdministrator())
                throw new UnauthorizedAccessException(
                    "Windows Administrator approval is required before installing a driver.");

            // The user may launch this executable directly, independent of
            // the Desktop's management mutex. All setup programs (regardless
            // of folder) must serialize PnP changes through this common lock.
            using var global = new Mutex(false,
                @"Global\AuralWeave.VirtualAudio.DriverInstall.v1");
            var acquired = false;
            try
            {
                try { acquired = global.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired)
                    throw new InvalidOperationException(
                        "Another AuralWeave Driver Setup is running.");

                var inventory = AuralWeaveVirtualDrivers.EnumerateInstalled();
                var readiness = VirtualDriverInstallation.CheckBundle(
                    inventory, role, AppContext.BaseDirectory);
                if (!readiness.CanInstall)
                    throw new InvalidOperationException(readiness.Description);
                if (SharedVirtualAudioActivityLease.IsActive(role))
                    throw new InvalidOperationException(
                        $"Virtual {role} is currently in use by an AuralWeave process.");

                // Only the two compiled-in hardware IDs may be created.
                var hardwareId = role == VirtualAudioEndpointRole.Input
                    ? AuralWeaveVirtualDrivers.InputHardwareId
                    : AuralWeaveVirtualDrivers.OutputHardwareId;
                var name = role == VirtualAudioEndpointRole.Input
                    ? "AuralWeave Virtual Input" : "AuralWeave Virtual Output";
                var inf = Path.GetFullPath(
                    Path.Combine(AppContext.BaseDirectory, "AuralWeaveVirtualAudio.inf"));

                Console.WriteLine($"Installing one Windows root-enumerated {name}...");
                var restartRequired = InstallRootEnumeratedDevice(hardwareId, name, inf);
                var actual = AuralWeaveVirtualDrivers.EnumerateInstalled();
                if (!actual.Any(x => x.Role == role && !x.IsLegacy))
                    throw new InvalidOperationException(
                        "Windows did not enumerate the expected AuralWeave device.");
                Console.WriteLine(restartRequired
                    ? "Driver installed; Windows reports that a reboot is required."
                    : "Driver installation returned successfully.");
                return 0;
            }
            finally
            {
                if (acquired) global.ReleaseMutex();
            }
        }
        catch (Win32Exception ex)
        {
            Console.Error.WriteLine($"Windows SetupAPI error {ex.NativeErrorCode}: {ex.Message}");
            return 5;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Driver installation not performed: {ex.Message}");
            return 1;
        }
    }

    private static void ShowUsage()
    {
        Console.WriteLine("AuralWeave Virtual Audio Setup (Windows 10+)");
        Console.WriteLine("  --check           inspect Windows PnP and local signed package (read-only)");
        Console.WriteLine("  --install-input   install the one Windows Virtual Input");
        Console.WriteLine("  --install-output  install the one Windows Virtual Output");
        Console.WriteLine("Uninstallation is managed by Windows Device Manager.");
        Console.WriteLine("The unsigned WDK developer prototype is not an installable package.");
    }

    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity)
            .IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool InstallRootEnumeratedDevice(
        string hardwareId, string deviceName, string fullInfPath)
    {
        // Adapted from the documented Windows SetupAPI root-enumeration
        // procedure. Never use INSTALLFLAG_FORCE or modify boot policy.
        var mediaClass = new Guid("4d36e96c-e325-11ce-bfc1-08002be10318");
        var info = SetupDiCreateDeviceInfoList(ref mediaClass, IntPtr.Zero);
        if (info == new IntPtr(-1))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "SetupDiCreateDeviceInfoList failed.");

        var device = new SpDevinfoData {
            Size = (uint)Marshal.SizeOf<SpDevinfoData>() };
        var registered = false;
        var success = false;
        try
        {
            // DICD_GENERATE_ID generates a new ROOT\...\000N PnP instance.
            // The matching hardware ID is assigned separately as REG_MULTI_SZ.
            var deviceSuffix = hardwareId.Substring("ROOT\\".Length);
            if (!SetupDiCreateDeviceInfo(info, deviceSuffix, ref mediaClass,
                deviceName, IntPtr.Zero, DicdGenerateId, ref device))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "SetupDiCreateDeviceInfo failed.");

            var multiSz = Encoding.Unicode.GetBytes(hardwareId + "\0\0");
            if (!SetupDiSetDeviceRegistryProperty(info, ref device,
                SpdrpHardwareId, multiSz, (uint)multiSz.Length))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "SetupDiSetDeviceRegistryProperty failed.");

            if (!SetupDiCallClassInstaller(DifRegisterDevice, info, ref device))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "DIF_REGISTERDEVICE failed.");
            registered = true;

            // The driver updater validates the PnP match/INF and Windows
            // signature policy. Never force-install an incompatible driver.
            if (!UpdateDriverForPlugAndPlayDevices(IntPtr.Zero, hardwareId,
                fullInfPath, 0, out var restartRequired))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "UpdateDriverForPlugAndPlayDevices failed.");

            success = true;
            return restartRequired;
        }
        finally
        {
            // Only roll back the exact node created by THIS failing setup
            // invocation. Never remove an existing AuralWeave or other device.
            if (registered && !success)
            {
                if (!SetupDiCallClassInstaller(DifRemove, info, ref device))
                    Console.Error.WriteLine(
                        "Rollback failed; inspect the new device in Windows Device Manager.");
            }
            SetupDiDestroyDeviceInfoList(info);
        }
    }

    private const uint DicdGenerateId = 0x00000001;
    private const uint SpdrpHardwareId = 0x00000001;
    private const uint DifRegisterDevice = 0x00000019;
    private const uint DifRemove = 0x00000005;

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        public uint Size;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiCreateDeviceInfoList(
        ref Guid classGuid, IntPtr hwndParent);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode,
        EntryPoint = "SetupDiCreateDeviceInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiCreateDeviceInfo(
        IntPtr deviceInfoSet, string deviceName, ref Guid classGuid,
        string deviceDescription, IntPtr hwndParent, uint creationFlags,
        ref SpDevinfoData deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode,
        EntryPoint = "SetupDiSetDeviceRegistryPropertyW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiSetDeviceRegistryProperty(
        IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData,
        uint property, byte[] propertyBuffer, uint propertyBufferSize);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiCallClassInstaller",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiCallClassInstaller(
        uint installFunction, IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData);

    [DllImport("newdev.dll", EntryPoint = "UpdateDriverForPlugAndPlayDevicesW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateDriverForPlugAndPlayDevices(
        IntPtr hwndParent, string hardwareId, string fullInfPath,
        uint installFlags, [MarshalAs(UnmanagedType.Bool)] out bool rebootRequired);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
}
