using System.IO;
using System.Text.Json;
using AuralWeave.Core;

namespace AuralWeave.Desktop;

/// <summary>Tracks DLLs explicitly loaded from this portable copy's plugin picker.</summary>
internal sealed class VstPluginRegistry
{
    private static readonly string BaseFolder = AppContext.BaseDirectory;
    private static readonly string FilePath = Path.Combine(BaseFolder, "settings", "vst-plugins.json");
    private readonly Dictionary<string, string> _known = Read();

    public static VstPluginReference ReferenceFor(string path, string sha256)
    {
        var relative = Path.GetRelativePath(BaseFolder, path);
        return new VstPluginReference(IsInside(relative) ? relative : path, sha256);
    }

    public static string? Resolve(VstPluginReference reference)
    {
        // Relative references are rooted at the app, so moving the portable
        // folder also moves its bundled plugin references.
        var stored = reference.Path;
        try
        {
            if (Path.IsPathRooted(stored) && !Path.IsPathFullyQualified(stored)) return null;
            var fullPath = Path.GetFullPath(stored, BaseFolder);
            if (!Path.IsPathRooted(stored) && !IsInside(Path.GetRelativePath(BaseFolder, fullPath)))
                return null;

            // Preset files reference files on local drive paths. Scan and
            // explicit Load plugin continue to support manually chosen folders.
            return fullPath.Length >= 3 && char.IsAsciiLetter(fullPath[0]) &&
                   fullPath[1] == ':' && fullPath[2] == Path.DirectorySeparatorChar
                ? fullPath : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    public bool IsKnown(VstPluginReference reference) =>
        _known.TryGetValue(reference.Path, out var hash) &&
        string.Equals(hash, reference.Sha256, StringComparison.OrdinalIgnoreCase);

    public void Remember(VstPluginReference reference)
    {
        _known[reference.Path] = reference.Sha256;

        var saved = Read();
        saved[reference.Path] = reference.Sha256;
        var folder = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(folder);
        var temp = Path.Combine(folder, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(saved));
            File.Move(temp, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static bool IsInside(string relative) =>
        !Path.IsPathRooted(relative) && relative != ".." &&
        !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private static Dictionary<string, string> Read()
    {
        try
        {
            if (File.Exists(FilePath) &&
                JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllBytes(FilePath)) is { } saved)
                return new Dictionary<string, string>(saved, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            // A missing or damaged list leaves plugin loading with the picker.
        }
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
