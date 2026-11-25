using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace AuralWeave.Audio.Windows;

/// <summary>
/// Production-package installer entry point. Never uses developer WDK build
/// outputs or unsigned .sys/.inf files. Until a trusted signed installer and
/// catalog are shipped next to the portable app, installation is unavailable.
/// </summary>
public static class VirtualDriverInstallation
{
    private const string BundleFolder = "AuralWeaveVirtualAudio";
    private const string InstallerFilename = "AuralWeaveVirtualAudioSetup.exe";
    private const string DriverInf = "AuralWeaveVirtualAudio.inf";
    private const string DriverSys = "AuralWeaveVirtualAudio.sys";
    private const string DriverCat = "AuralWeaveVirtualAudio.cat";

    public sealed record Readiness(bool CanInstall, string Description);

    public static Readiness Check(
        IReadOnlyList<AuralWeaveVirtualDevice> installed,
        VirtualAudioEndpointRole role)
    {
        ArgumentNullException.ThrowIfNull(installed);
        if (role is not (VirtualAudioEndpointRole.Input or VirtualAudioEndpointRole.Output))
            throw new ArgumentOutOfRangeException(nameof(role));

        if (!AuralWeaveVirtualDrivers.MayInstall(installed, role))
        {
            var existing = installed.Where(d => d.Role == role).ToArray();
            if (existing.Any(d => !d.IsDriverBound))
                return new(false, $"Virtual {role}: device ID already exists but driver is not fully registered. Inspect it in Windows Device Manager.");
            if (existing.Any(d => d.IsLegacy))
                return new(false, $"Virtual {role}: obsolete AuralWeave device ID exists. Manage it in Windows Device Manager before installing the current version.");
            return new(false, $"Virtual {role}: installed (1/1).");
        }

        var folder = Path.Combine(AppContext.BaseDirectory, "drivers", BundleFolder);
        // Portable application folder is the ONLY package source. No Internet,
        // downloaded binary, test-signed WDK build, temp-file or arbitrary path.
        if (!Directory.Exists(folder))
            return new(false, $"Virtual {role}: not installed. Production signed driver package is not yet included.");

        var setup = Path.Combine(folder, InstallerFilename);
        var inf = Path.Combine(folder, DriverInf);
        var sys = Path.Combine(folder, DriverSys);
        var cat = Path.Combine(folder, DriverCat);
        if (!File.Exists(setup) || !File.Exists(inf) ||
            !File.Exists(sys) || !File.Exists(cat))
            return new(false, $"Virtual {role}: not installed. Driver package files are incomplete.");

        // Both Windows trust and product-identity checks are mandatory before
        // enabling an elevated process. The Windows PnP installer independently
        // validates the catalog association and full INF/SYS package.
        if (!IsTrustedAuralWeaveCode(setup) || !VerifyAuthenticode(cat))
            return new(false, $"Virtual {role}: not installed. Production driver signature could not be verified.");

        return new(true, $"Virtual {role}: not installed. Verified signed driver package is available.");
    }

    /// <summary>
    /// Launch only an independently signed, production installer with Windows
    /// UAC. The installer MUST recheck exact PnP identity/role/certificates
    /// under elevation and register the root devnode. PnPUtil /add-driver alone
    /// cannot create a root-enumerated audio device.
    /// </summary>
    public static async Task<bool> InstallAsync(VirtualAudioEndpointRole role)
    {
        var installed = AuralWeaveVirtualDrivers.EnumerateInstalled();
        var eligibility = Check(installed, role);
        if (!eligibility.CanInstall)
            throw new InvalidOperationException(eligibility.Description);
        if (SharedVirtualAudioActivityLease.IsActive(role))
            throw new InvalidOperationException(
                $"Virtual {role} is currently in use by another portable copy.");

        using var managementLease = VirtualDeviceControlLease.TryAcquire(
            role, waitMilliseconds: 500)
            ?? throw new InvalidOperationException(
                $"Another AuralWeave instance is managing Virtual {role}.");

        // Recheck after acquiring the global lock to block concurrent
        // management attempts from cooperating application instances.
        var fresh = Check(AuralWeaveVirtualDrivers.EnumerateInstalled(), role);
        if (!fresh.CanInstall || SharedVirtualAudioActivityLease.IsActive(role))
            throw new InvalidOperationException(
                $"Virtual {role} state changed. Refresh the Windows device list.");

        var setup = Path.Combine(AppContext.BaseDirectory, "drivers",
            BundleFolder, InstallerFilename);
        var start = new ProcessStartInfo
        {
            FileName = setup,
            WorkingDirectory = Path.GetDirectoryName(setup)!,
            UseShellExecute = true,
            Verb = "runas"
        };
        start.ArgumentList.Add(role == VirtualAudioEndpointRole.Input
            ? "--install-input" : "--install-output");

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Windows refused to start the signed installer.");
        await process.WaitForExitAsync().ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new Win32Exception(
                process.ExitCode, $"Windows driver installer failed (exit {process.ExitCode}).");
        return AuralWeaveVirtualDrivers.EnumerateInstalled().Any(x =>
            x.Role == role && !x.IsLegacy);
    }

    private static bool IsTrustedAuralWeaveCode(string path)
    {
        // Reject files that are not valid Authenticode-signed binaries with a
        // trusted Windows chain. Fail closed, including malformed certificates.
        if (!File.Exists(path) || !VerifyAuthenticode(path))
            return false;
        try
        {
            using var signer = X509Certificate.CreateFromSignedFile(path);
            var subject = signer.Subject;
            return subject.Contains("AuralWeave", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    // WINTRUST_ACTION_GENERIC_VERIFY_V2 validates the embedded signature
    // (EXE or signed CAT) using the local Windows trust provider. No bypass
    // of signature enforcement or Secure Boot is attempted.
    private static readonly Guid WintrustAction =
        new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
    private const uint WtdUiNone = 2;
    private const uint WtdRevocationWholeChain = 1;
    private const uint WtdChoiceFile = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string Path;
        public IntPtr File;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint Size;
        public IntPtr PolicyCallback;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }

    [DllImport("wintrust.dll", PreserveSig = true, CharSet = CharSet.Unicode)]
    private static extern uint WinVerifyTrust(
        IntPtr hwnd, [In] ref Guid actionId, [In] ref WinTrustData trustData);

    private static bool VerifyAuthenticode(string path)
    {
        var info = new WinTrustFileInfo
        {
            Size = (uint)Marshal.SizeOf<WinTrustFileInfo>(), Path = path
        };
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            var trustData = new WinTrustData
            {
                Size = (uint)Marshal.SizeOf<WinTrustData>(),
                UiChoice = WtdUiNone,
                RevocationChecks = WtdRevocationWholeChain,
                UnionChoice = WtdChoiceFile,
                FileInfo = ptr
            };
            var action = WintrustAction;
            return WinVerifyTrust(IntPtr.Zero, ref action, ref trustData) == 0;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            Marshal.DestroyStructure<WinTrustFileInfo>(ptr);
            Marshal.FreeHGlobal(ptr);
        }
    }
}
