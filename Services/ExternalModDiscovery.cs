using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace StellarModManager.Services;

internal sealed record MelonMetadata(string Name, string Author, string Version);

internal sealed record ExternalDll(string Path, long Size, DateTime ModifiedUtc, string? LinkTarget,
    string? Hash, List<MelonMetadata> Melons);

internal static class ExternalModDiscovery
{
    private sealed record CachedScan(int Version, List<ExternalDll> Files);
    private const int CacheVersion = 1;

    public static List<ExternalDll> Scan(string modsDirectory, string cachePath,
        IReadOnlySet<string> ownedFiles, IReadOnlySet<string> changedPaths, bool invalidate,
        CancellationToken cancellationToken)
    {
        var cache = new Dictionary<string, ExternalDll>(StringComparer.Ordinal);
        if (!invalidate && ReadCache<CachedScan>(cachePath) is { Version: CacheVersion, Files: not null } saved)
        {
            foreach (var entry in saved.Files)
                cache.TryAdd(entry.Path, entry);
        }

        var scanned = new List<ExternalDll>();
        if (!ProfileFileTransaction.IsRegularDirectory(modsDirectory))
            return scanned;

        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 };
        var directories = new Stack<string>();
        directories.Push(modsDirectory);
        while (directories.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!ProfileFileTransaction.IsRegularDirectory(directory))
                    continue;

                foreach (var path in Directory.EnumerateFileSystemEntries(directory, "*", options))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var attributes = ProfileFileTransaction.Attributes(path);
                    if (attributes is not { } flags)
                        continue;
                    if (flags.HasFlag(FileAttributes.Directory))
                    {
                        if (!flags.HasFlag(FileAttributes.ReparsePoint))
                            directories.Push(path);
                        continue;
                    }

                    if (!Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var relative = RelativePath(modsDirectory, path);
                    if (ownedFiles.Contains(relative))
                        continue;

                    var file = ReadFile(path, relative, cache, changedPaths);
                    if (file != null)
                        scanned.Add(file);
                }
            }
            catch (IOException){}
            catch (UnauthorizedAccessException){}
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (invalidate || cache.Count != scanned.Count || scanned.Any(item => !cache.TryGetValue(item.Path, out var old) || old != item))
            WriteCache(cachePath, new CachedScan(CacheVersion, scanned));

        return [.. scanned.Where(file => file.Melons.Count > 0).OrderBy(file => file.Path, StringComparer.Ordinal)];
    }

    internal static string RelativePath(string modsDirectory, string path)
    {
        var relative = Path.GetRelativePath(modsDirectory, path);
        return relative == "." ? "Mods" : "Mods/" + relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static ExternalDll? ReadFile(string path, string relative, Dictionary<string, ExternalDll> cache,
        IReadOnlySet<string> changedPaths)
    {
        try
        {
            var info = new FileInfo(path);
            var link = info.LinkTarget;
            var target = link == null ? info : info.ResolveLinkTarget(returnFinalTarget: true) as FileInfo;
            if (target is not { Exists: true })
                return null;

            var size = target.Length;
            var modified = target.LastWriteTimeUtc;
            if (!changedPaths.Contains(relative)
                && !changedPaths.Any(parent => relative.StartsWith(parent + "/", StringComparison.Ordinal))
                && cache.TryGetValue(relative, out var saved)
                && size == saved.Size && modified == saved.ModifiedUtc && link == saved.LinkTarget)
                return saved;

            using var stream = File.OpenRead(path);
            var melons = ReadMelonInfo(stream);
            string? hash = null;
            if (melons.Count > 0)
            {
                stream.Position = 0;
                hash = Convert.ToHexString(SHA256.HashData(stream));
            }

            target.Refresh();
            if (!target.Exists || target.Length != size || target.LastWriteTimeUtc != modified
                || new FileInfo(path).LinkTarget != link)
                return null;

            return new ExternalDll(relative, size, modified, link, hash, melons);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static List<MelonMetadata> ReadMelonInfo(Stream stream)
    {
        var melons = new List<MelonMetadata>();
        try
        {
            using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!pe.HasMetadata)
                return melons;

            var reader = pe.GetMetadataReader();
            if (!reader.IsAssembly)
                return melons;

            foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
            {
                try
                {
                    var attribute = reader.GetCustomAttribute(handle);
                    if (attribute.Constructor.Kind != HandleKind.MemberReference)
                        continue;

                    var constructor = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                    if (constructor.Parent.Kind != HandleKind.TypeReference
                        || !reader.StringComparer.Equals(constructor.Name, ".ctor"))
                        continue;

                    var type = reader.GetTypeReference((TypeReferenceHandle)constructor.Parent);
                    if (!reader.StringComparer.Equals(type.Namespace, "MelonLoader")
                        || !reader.StringComparer.Equals(type.Name, "MelonInfoAttribute")
                        || type.ResolutionScope.Kind != HandleKind.AssemblyReference
                        || !reader.StringComparer.Equals(
                            reader.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope).Name, "MelonLoader"))
                        continue;

                    var signature = reader.GetBlobReader(constructor.Signature);
                    signature.ReadSignatureHeader();
                    var parameters = signature.ReadCompressedInteger();
                    if (parameters is not (4 or 5 or 7 or 8))
                        continue;

                    var value = reader.GetBlobReader(attribute.Value);
                    if (value.ReadUInt16() != 1 || string.IsNullOrEmpty(value.ReadSerializedString()))
                        continue;

                    var name = value.ReadSerializedString() ?? "UNKNOWN";
                    string? version;
                    string? author;
                    if (parameters <= 5)
                    {
                        version = value.ReadSerializedString();
                        author = value.ReadSerializedString();
                    }
                    else
                    {
                        version = $"{value.ReadInt32()}.{value.ReadInt32()}.{value.ReadInt32()}";
                        if (parameters == 8)
                            version += value.ReadSerializedString();
                        author = value.ReadSerializedString();
                    }

                    if (parameters != 4)
                        value.ReadSerializedString();
                    value.ReadUInt16();

                    melons.Add(new MelonMetadata(name, author ?? "UNKNOWN",
                        string.IsNullOrEmpty(version) ? "1.0.0" : version));
                }
                catch (BadImageFormatException){}
            }
        }
        catch (BadImageFormatException){}

        return melons;
    }

    internal static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    internal static T? ReadCache<T>(string path) where T : class
    {
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    internal static void WriteCache<T>(string path, T value)
    {
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = File.Create(temporary))
                JsonSerializer.Serialize(stream, value);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException){}
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
