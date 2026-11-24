using System.IO;
using System.Text.Json;
using System.Windows;

namespace AuralWeave.Desktop;

/// <summary>
/// UI-only appearance system: no audio engine configuration or latency impact.
/// Relative settings path keeps the Windows portable installation relocatable.
/// </summary>
internal static class SkinManager
{
    public const string DefaultSkin = "MonochromeDark";
    public const string GraphiteSkin = "Graphite";

    private static string SettingsFile => Path.Combine(
        AppContext.BaseDirectory, "settings", "appearance.json");

    internal static string Load()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return DefaultSkin;
            var item = JsonSerializer.Deserialize<Appearance>(File.ReadAllText(SettingsFile));
            return IsValid(item?.Skin) ? item!.Skin : DefaultSkin;
        }
        catch (Exception)
        {
            return DefaultSkin;
        }
    }

    internal static void Apply(string skin, bool save)
    {
        if (!IsValid(skin)) throw new ArgumentException("Unknown skin.", nameof(skin));
        var resources = Application.Current.Resources.MergedDictionaries;
        var target = new ResourceDictionary
        {
            Source = new Uri($"Themes/{skin}.xaml", UriKind.Relative)
        };
        if (resources.Count == 0) resources.Add(target);
        else resources[0] = target;

        if (!save) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            var json = JsonSerializer.Serialize(
                new Appearance(skin), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
        }
        catch (IOException)
        {
            // The UI can still use a skin if the installation folder is read-only.
        }
        catch (UnauthorizedAccessException)
        {
            // Portable save is best-effort; no silent fallback to another drive.
        }
    }

    private static bool IsValid(string? skin) =>
        skin is DefaultSkin or GraphiteSkin;

    private sealed record Appearance(string Skin);
}
