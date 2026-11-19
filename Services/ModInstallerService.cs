using StellarModManager.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace StellarModManager.Services;

internal sealed record ProfilePackage(InstalledModInfo Mod, List<string> Files);
internal sealed record StagedMod(ProfilePackage Package, string Directory);

internal class ModInstallerService
{
    public StagedMod Extract(string zipPath, string destination, string expectedId)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entries = archive.Entries
            .Where(entry => entry.Name.Length > 0 && !entry.FullName.EndsWith('\\'))
            .Select(entry => (Entry: entry, Path: NormalizePath(entry.FullName)))
            .ToList();

        var metadata = entries
            .Where(entry => IsMetadata(entry.Path))
            .OrderBy(entry => entry.Path.Count(character => character == '/'))
            .FirstOrDefault();
        if (metadata.Entry == null)
            throw new InvalidDataException("Invalid mod package: mod.json missing.");

        using var stream = metadata.Entry.Open();
        var mod = ReadMetadata(stream);
        if (!string.Equals(mod.Id, expectedId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Expected mod '{expectedId}', but the package contains '{mod.Id}'.");

        var prefix = metadata.Path[..(metadata.Path.LastIndexOf('/') + 1)];
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (entry, archivePath) in entries)
        {
            if (!archivePath.StartsWith(prefix, StringComparison.Ordinal) || IsInternalFile(archivePath))
                continue;

            var path = GameRelativePath(archivePath[prefix.Length..]);
            if (!paths.Add(path))
                throw new InvalidDataException($"Duplicate package path: {path}");

            var target = Path.Combine(destination, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target);
        }

        return new StagedMod(new ProfilePackage(mod, [.. paths]), destination);
    }

    internal static InstalledModInfo ReadMetadata(Stream stream)
    {
        var mod = JsonSerializer.Deserialize<InstalledModInfo>(stream);
        if (mod == null || string.IsNullOrWhiteSpace(mod.Id))
            throw new InvalidDataException("Invalid mod package: missing mod ID.");
        return mod;
    }

    internal static bool IsInternalFile(string path) =>
        IsMetadata(path) || Path.GetFileName(path).Equals(".deployment.json", StringComparison.OrdinalIgnoreCase);

    private static bool IsMetadata(string path) =>
        Path.GetFileName(path).Equals("mod.json", StringComparison.OrdinalIgnoreCase);

    internal static string GameRelativePath(string path)
    {
        path = NormalizePath(path);
        if (path.Equals("Mods", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A mod package must use Mods as a directory.");
        return InMods(path) ? "Mods" + path[4..] : path;
    }

    internal static bool InMods(string path) => path.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase);

    internal static string NormalizePath(string path)
    {
        var parts = path.Replace('\\', '/').Split('/');
        foreach (var part in parts)
        {
            var name = part.Split('.')[0].ToUpperInvariant();
            if (part.Length == 0 || part.EndsWith('.') || part.EndsWith(' ')
                || part.Any(character => character < ' ' || "<>:\"|?*".Contains(character))
                || part.StartsWith(".smm-", StringComparison.OrdinalIgnoreCase)
                || name is "CON" or "PRN" or "AUX" or "NUL"
                || (name.Length == 4 && (name.StartsWith("COM") || name.StartsWith("LPT"))
                    && "0123456789¹²³".Contains(name[3])))
                throw new InvalidDataException($"Invalid mod file path: {path}");
        }

        return string.Join('/', parts);
    }
}
