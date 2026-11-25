using System.Diagnostics;
using AuralWeave.Audio.Windows;

/// <summary>
/// No kernel driver, no privileged broker: verifies Windows per-owner HANDLE
/// liveness after a separately launched Portable-style process is force-killed.
/// The parent retains the Shared Memory mapping but must still discover that
/// its actual creator is gone. Also checks that the surviving stream continues.
/// </summary>
internal static class VirtualBusRecoveryTests
{
    private static async Task<(Process Child, string Mapping)> LaunchOwnerAsync(uint role)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "AuralWeave.Diagnostics.dll"));
        psi.ArgumentList.Add("--bridge-owner-child");
        psi.ArgumentList.Add(role.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var child = Process.Start(psi) ??
            throw new InvalidOperationException("Could not launch Virtual PCM owner process.");
        try
        {
            for (var i = 0; i < 5; i++)
            {
                var line = await child.StandardOutput.ReadLineAsync()
                    .WaitAsync(TimeSpan.FromSeconds(8));
                if (line is null)
                    throw new InvalidOperationException(
                        "Virtual PCM owner quit before publishing a mapping: " +
                        await child.StandardError.ReadToEndAsync());
                if (line.StartsWith("OWNER_MAP=", StringComparison.Ordinal))
                    return (child, line["OWNER_MAP=".Length..]);
            }
            throw new InvalidOperationException(
                "Virtual PCM child did not publish its mapping name.");
        }
        catch
        {
            await TerminateAsync(child);
            child.Dispose();
            throw;
        }
    }

    private static async Task TerminateAsync(Process child)
    {
        if (!child.HasExited)
            child.Kill(entireProcessTree: true);
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8));
    }

    public static async Task RunAsync()
    {
        foreach (var role in new[] { 1u, 2u })
        {
            using var survivor = NativeVirtualAudioBridge.Create(
                role, 1, 48000, 2, 512);
            using var receiver = NativeVirtualAudioBridge.Create(
                role, 1, 48000, 2, 512);
            using var bus = new SharedVirtualAudioBus(role, 48000, 2, 64);
            bus.AddContributor(survivor.Name);
            bus.AddListener(receiver.Name);

            var (child, name) = await LaunchOwnerAsync(role);
            using (child)
            {
                try
                {
                    if (role == 1u)
                        bus.AddContributor(name);
                    else
                        bus.AddListener(name);

                    using (var livePeer = NativeVirtualAudioBridge.Attach(name, role, 1))
                    {
                        if (livePeer.GetStats().ClosedByOwner != 0)
                            throw new InvalidOperationException(
                                "Child's PCM owner was reported closed while alive.");
                        await TerminateAsync(child);
                        if (livePeer.GetStats().ClosedByOwner == 0)
                            throw new InvalidOperationException(
                                "Killed PCM owner was not detected through its OS handle.");
                    }

                    // Exercise explicit cleanup for Input and automatic,
                    // periodic cleanup for Output (16 bounded Pump calls).
                    var removed = 0;
                    if (role == 1u)
                        removed = bus.PruneDisconnected();
                    else
                    {
                        for (var tick = 0; tick < 16; tick++)
                            bus.Pump();
                        removed = (int)bus.DisconnectedListeners;
                    }
                    if (removed != 1 ||
                        bus.Contributors != 1 || bus.Listeners != 1 ||
                        (role == 1u ? bus.DisconnectedContributors
                                    : bus.DisconnectedListeners) != 1)
                        throw new InvalidOperationException(
                            "Stale PCM client was not removed without touching healthy clients.");

                    const float expected = 0.375f;
                    if (survivor.Write([expected, -expected, expected, -expected]) != 2 ||
                        bus.Pump() != 2)
                        throw new InvalidOperationException(
                            "Surviving Portable session did not keep sending audio.");
                    var got = new float[4];
                    if (receiver.Read(got) != 2 ||
                        got[0] != expected || got[1] != -expected)
                        throw new InvalidOperationException(
                            "Surviving Portable session did not keep receiving audio.");
                    Console.WriteLine($"PASS: role={role}, abrupt owner death detected and surviving PCM continues.");
                }
                finally
                {
                    await TerminateAsync(child);
                }
            }
        }
        Console.WriteLine("SHARED_VIRTUAL_PCM_CRASH_RECOVERY=PASS (no driver installed)");
    }
}
