using System.Reflection.PortableExecutable;

namespace AuralWeave.Audio.Windows;

public enum VstPluginFormat { VST2, VST3 }

public sealed record VstPluginCandidate(string Name, string Path, VstPluginFormat Format)
{
    public string DisplayName => $"{Name} · {Format}";
}

public static class VstPluginCatalog
{
    public static IReadOnlyList<VstPluginCandidate> Scan(string folder, bool includeStandardVst3 = false)
    {
        var root = Path.GetFullPath(folder);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException("The VST plugins folder does not exist.");

        var candidates = new Dictionary<string, VstPluginCandidate>(StringComparer.OrdinalIgnoreCase);
        ScanTree(root, candidates, vst3Only: false);
        if (includeStandardVst3)
        {
            var standard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), "VST3");
            if (Directory.Exists(standard)) ScanTree(standard, candidates, vst3Only: true);
        }
        return candidates.Values.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Format).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void ScanTree(string root, Dictionary<string, VstPluginCandidate> candidates, bool vst3Only)
    {
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var folder))
        {
            if (folder.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase))
            {
                var file = Path.Combine(folder, "Contents", "x86_64-win", Path.GetFileName(folder));
                Add(file, Path.GetFileNameWithoutExtension(folder));
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(folder, "*", options))
            {
                if (file.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase) ||
                    (!vst3Only && file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                    Add(file, Path.ChangeExtension(Path.GetRelativePath(root, file), null));
            }
            foreach (var child in Directory.EnumerateDirectories(folder, "*", options)) pending.Push(child);
        }

        void Add(string file, string label)
        {
            if (!candidates.ContainsKey(file) && Inspect(file) is { } plugin)
                candidates.Add(file, plugin with { Name = label });
        }
    }

    public static VstPluginCandidate? Inspect(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && HasExport(path, "VSTPluginMain\0"u8))
            return new VstPluginCandidate(name, path, VstPluginFormat.VST2);
        if (path.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase) && HasExport(path, "GetPluginFactory\0"u8))
            return new VstPluginCandidate(name, path, VstPluginFormat.VST3);
        return null;
    }

    public static string FolderFor(VstPluginCandidate plugin)
    {
        var directory = new FileInfo(plugin.Path).Directory!;
        // A bundled VST3 module is under the bundle's Contents/x86_64-win folder.
        if (plugin.Format == VstPluginFormat.VST3 &&
            directory.Parent?.Parent is { } bundle && bundle.Name.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase))
            return bundle.Parent?.FullName ?? bundle.FullName;
        return directory.FullName;
    }

    private static bool HasExport(string path, ReadOnlySpan<byte> entry)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            var headers = pe.PEHeaders;
            if (headers.CoffHeader.Machine != Machine.Amd64 ||
                (headers.CoffHeader.Characteristics & Characteristics.Dll) == 0 ||
                headers.PEHeader is not { Magic: PEMagic.PE32Plus } header)
                return false;

            // Read only the PE export table. Scanning never maps or runs a DLL.
            var exports = header.ExportTableDirectory;
            if (exports.RelativeVirtualAddress <= 0 || exports.Size < 40)
                return false;
            var export = pe.GetSectionData(exports.RelativeVirtualAddress);
            if (export.Length < 40) return false;
            var info = export.GetReader(24, 12);
            var nameCount = info.ReadInt32();
            info.ReadInt32(); // address of function pointers
            var namesRva = info.ReadInt32();
            if (nameCount is < 1 or > 65536 || namesRva <= 0)
                return false;
            var table = pe.GetSectionData(namesRva);
            if (nameCount > table.Length / sizeof(int)) return false;

            var names = table.GetReader();
            for (var i = 0; i < nameCount; i++)
            {
                var rva = names.ReadInt32();
                if (rva <= 0) continue;
                var item = pe.GetSectionData(rva);
                if (item.Length >= entry.Length &&
                    item.GetContent(0, entry.Length).AsSpan().SequenceEqual(entry))
                    return true;
            }
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException
                                      or UnauthorizedAccessException or ArgumentException)
        {
            // A missing, inaccessible, or invalid file is not a VST candidate.
        }
        return false;
    }
}
