using System.Reflection.PortableExecutable;

namespace AuralWeave.Audio.Windows;

public sealed record Vst2Candidate(string Name, string Path);

public static class Vst2PluginCatalog
{
    public static IReadOnlyList<Vst2Candidate> Scan(string folder)
    {
        var root = Path.GetFullPath(folder);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException("The VST plugins folder does not exist.");

        var candidates = new List<Vst2Candidate>();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        foreach (var file in Directory.EnumerateFiles(root, "*.dll", options))
        {
            if (!IsVst2Plugin(file)) continue;
            var relative = Path.GetRelativePath(root, file);
            candidates.Add(new Vst2Candidate(Path.ChangeExtension(relative, null), file));
        }

        return candidates.OrderBy(candidate => candidate.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public static bool IsVst2Plugin(string path)
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
            ReadOnlySpan<byte> entry = "VSTPluginMain\0"u8;
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
