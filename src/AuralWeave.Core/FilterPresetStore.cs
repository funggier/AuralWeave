using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AuralWeave.Core;

/// <summary>Only filter state and order are persisted. Audio source/output IDs are never serialized.</summary>
public sealed record FilterPresetStage(FilterKind Kind, bool Enabled, float Parameter,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    EqualizerSettings? Equalizer = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CompressorSettings? Compressor = null)
{
    public FilterStage Restore() =>
        new(Guid.NewGuid(), Kind, Enabled, Parameter, Equalizer, Compressor);
}
public sealed record FilterPresetDocument(int SchemaVersion, string Name,
    List<FilterPresetStage> Filters);

public sealed record FilterPresetItem(string Name, string FilePath)
{
    public override string ToString() => Name;
}

public sealed class FilterPresetStore
{
    public const int CurrentSchemaVersion = 2;
    private const int MaxPresetBytes = 128 * 1024;
    public const string Extension = ".awfilter";
    private static readonly JsonSerializerOptions Options = MakeOptions();

    public string Folder { get; }

    public FilterPresetStore(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            throw new ArgumentException("A preset folder is required.", nameof(folder));
        Folder = Path.GetFullPath(folder);
    }

    private static JsonSerializerOptions MakeOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter<FilterKind>(
            JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    public IReadOnlyList<FilterPresetItem> List()
    {
        if (!Directory.Exists(Folder)) return [];
        var result = new List<FilterPresetItem>();
        foreach (var path in Directory.EnumerateFiles(Folder, "*" + Extension))
        {
            try
            {
                var preset = ReadFile(path);
                result.Add(new FilterPresetItem(preset.Name, path));
            }
            catch (Exception ex) when (ex is IOException or JsonException or
                                       ArgumentException or UnauthorizedAccessException)
            {
                // One damaged preset must not prevent loading valid presets.
            }
        }
        return result.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public FilterPresetDocument Create(string name, IEnumerable<FilterStage> filters)
    {
        var cleanName = ValidateName(name);
        var stages = filters.ToArray();
        FilterStageRules.Validate(stages);
        return new FilterPresetDocument(CurrentSchemaVersion, cleanName,
            stages.Select(x => new FilterPresetStage(
                x.Kind, x.Enabled, x.Parameter, x.Equalizer, x.Compressor)).ToList());
    }

    public static List<FilterStage> Restore(FilterPresetDocument preset)
    {
        Validate(preset);
        return preset.Filters.Select(x => x.Restore()).ToList();
    }

    public FilterPresetItem Save(FilterPresetDocument preset, bool overwrite = false)
    {
        Validate(preset);
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, ValidateName(preset.Name) + Extension);
        WriteFile(path, preset, overwrite);
        return new FilterPresetItem(preset.Name, path);
    }

    public static FilterPresetDocument ReadFile(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 8192, FileOptions.SequentialScan);
        if (input.Length is < 1 or > MaxPresetBytes)
            throw new InvalidDataException("Preset is empty or exceeds 128 KiB.");
        var preset = JsonSerializer.Deserialize<FilterPresetDocument>(input, Options)
            ?? throw new InvalidDataException("Preset file was empty.");
        Validate(preset);
        return preset;
    }

    public static void WriteFile(string path, FilterPresetDocument preset,
                                  bool overwrite = false)
    {
        Validate(preset);
        var data = JsonSerializer.SerializeToUtf8Bytes(preset, Options);
        if (data.Length > MaxPresetBytes)
            throw new InvalidDataException("Preset exceeds 128 KiB.");
        var full = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(full)
            ?? throw new ArgumentException("Missing destination folder.", nameof(path));
        Directory.CreateDirectory(folder);
        if (!overwrite && File.Exists(full))
            throw new IOException("A preset with this name already exists.");

        // Never replace the destination with a partial serialized document.
        var temporary = Path.Combine(folder, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllBytes(temporary, data);
            File.Move(temporary, full, overwrite);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static void Validate(FilterPresetDocument? preset)
    {
        if (preset is null || preset.SchemaVersion is not (1 or CurrentSchemaVersion) ||
            preset.Filters is null)
            throw new InvalidDataException("Unsupported or malformed filter preset format.");
        ValidateName(preset.Name);
        if (preset.Filters.Count > 64)
            throw new InvalidDataException("Too many filter slots.");
        foreach (var stage in preset.Filters)
        {
            if (stage is null)
                throw new InvalidDataException("Filter stage cannot be null.");
            if (preset.SchemaVersion == 1 &&
                (stage.Kind > FilterKind.Limiter || stage.Equalizer is not null || stage.Compressor is not null))
                throw new InvalidDataException("Preset uses filter settings introduced in schema 2.");
        }
        FilterStageRules.Validate(preset.Filters.Select(stage => stage.Restore()));
    }

    private static string ValidateName(string? name)
    {
        var result = name?.Trim();
        if (string.IsNullOrWhiteSpace(result) || result.Length > 80 ||
            result is "." or ".." || result.EndsWith('.') || result.EndsWith(' ') ||
            result.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            result.Contains(Path.DirectorySeparatorChar) ||
            result.Contains(Path.AltDirectorySeparatorChar))
            throw new ArgumentException("Preset name must be 1–80 filename-safe characters.");
        return result;
    }
}
