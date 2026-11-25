using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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

    // This is intentionally EMPTY until a production publisher signing
    // certificate is obtained and independently verified. Do not allow an
    // arbitrary trusted publisher whose subject happens to say AuralWeave
    // to gain Administrator privileges via the Portable app.
    // To enable production installation, pin the SHA-256 hash of the
    // actual publisher certificate's DER bytes here during a signed release.
    private const string ApprovedInstallerCertificateSha256 = "";

    public sealed record Readiness(bool CanInstall, string Description);

    public static Readiness Check(
        IReadOnlyList<AuralWeaveVirtualDevice> installed,
        VirtualAudioEndpointRole role) =>
        CheckBundle(installed, role,
            Path.Combine(AppContext.BaseDirectory, "drivers", BundleFolder));

    /// <summary>
    /// Used by the future independently signed elevated installer. A signed
    /// helper lives IN the bundle, so its base directory differs from the
    /// Portable desktop's base directory. Re-validate from that location.
    /// </summary>
    public static Readiness CheckBundle(
        IReadOnlyList<AuralWeaveVirtualDevice> installed,
        VirtualAudioEndpointRole role,
        string packageDirectory)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        if (role is not (VirtualAudioEndpointRole.Input or VirtualAudioEndpointRole.Output))
            throw new ArgumentOutOfRangeException(nameof(role));

        var existing = installed.Where(device => device.Role == role).ToArray();
        if (existing.Length > 1)
            return new(false, $"Virtual {role}: {existing.Length} matching PnP devices found. Inspect them in Windows Device Manager.");
        if (existing.Length == 1)
        {
            var device = existing[0];
            return new(false, $"Virtual {role}: {device.InstallationStatus}.");
        }

        // The desktop supplies a strictly relative bundle path; the
        // elevated helper supplies its own base directory. This method
        // never downloads or selects any arbitrary third-party package.
        var folder = Path.GetFullPath(packageDirectory);
        if (!Directory.Exists(folder))
            return new(false, $"Virtual {role}: not installed. Production signed driver package is not yet included.");

        var setup = Path.Combine(folder, InstallerFilename);
        var inf = Path.Combine(folder, DriverInf);
        var sys = Path.Combine(folder, DriverSys);
        var cat = Path.Combine(folder, DriverCat);
        if (!File.Exists(setup) || !File.Exists(inf) ||
            !File.Exists(sys) || !File.Exists(cat))
            return new(false, $"Virtual {role}: not installed. Driver package files are incomplete.");

        // Production release must first pin its actual publisher signer.
        // A trusted EXE from some unrelated vendor is NOT sufficient.
        if (ApprovedInstallerCertificateSha256.Length != 64)
            return new(false, $"Virtual {role}: not installed. No approved production driver publisher is configured.");

        // Both Windows trust and exact publisher pin are mandatory before
        // enabling UAC. The elevated installer must independently validate
        // the catalog/INF/SYS relationship and certificate revocation.
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
        return AuralWeaveVirtualDrivers.IsRoleOperational(
            AuralWeaveVirtualDrivers.EnumerateInstalled(), role);
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
            var publisherHash = SHA256.HashData(signer.GetRawCertData());
            var approved = Convert.FromHexString(ApprovedInstallerCertificateSha256);
            return CryptographicOperations.FixedTimeEquals(publisherHash, approved);
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
