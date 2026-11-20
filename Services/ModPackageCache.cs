using StellarModManager.Models;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace StellarModManager.Services;

internal sealed record ModPackage(string Folder, InstalledModInfo Mod);

internal sealed class ModPackageCache(string appDirectory)
{
    private readonly string library = Path.Combine(appDirectory, "Library");
    private readonly string cache = Path.Combine(AppPaths.UserDataDirectory, "Packages");

    internal static string DirectoryKey(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    internal ModPackage? Find(string id, string? version = null)
    {
        var root = Path.Combine(cache, DirectoryKey(id.ToUpperInvariant()));
        var candidates = version != null
            ? new[] { Path.Combine(root, DirectoryKey(version)) }
            : Directory.Exists(root) ? Directory.GetDirectories(root) : Array.Empty<string>();

        if (id.Length > 0 && id is not "." and not ".." && id.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
            !id.Contains('/') && !id.Contains('\\') && !Path.IsPathRooted(id))
        {
            candidates = candidates.Append(Path.Combine(library, id)).ToArray();
        }

        return candidates.Select(TryReadPackage)
            .OfType<ModPackage>()
            .Where(p => string.Equals(p.Mod.Id, id, StringComparison.OrdinalIgnoreCase) &&
                        (version == null || p.Mod.Version == version))
            .OrderByDescending(p => Version.TryParse(p.Mod.Version, out var parsed) ? parsed : null)
            .FirstOrDefault();
    }

    internal static ModPackage? ReadPackage(string folder)
    {
        var metadata = Path.Combine(folder, "mod.json");
        if (!File.Exists(metadata))
            return null;

        var mod = JsonSerializer.Deserialize<InstalledModInfo>(File.ReadAllText(metadata));
        return mod == null ? null : new ModPackage(folder, mod);
    }

    private static ModPackage? TryReadPackage(string folder)
    {
        try
        {
            return ReadPackage(folder);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    internal async Task<ModPackage> GetAsync(OnlineModInfo mod, ModRepositoryService repository, IProgress<double> progress)
    {
        var id = mod.Id;
        var version = mod.Version;
        var name = mod.Name;
        var needs = mod.Needs?.ToList() ?? new();
        var existing = Find(id, version);
        if (existing != null)
            return existing;

        var root = Path.Combine(cache, DirectoryKey(id.ToUpperInvariant()));
        var destination = Path.Combine(root, DirectoryKey(version));
        Directory.CreateDirectory(root);
        var work = Directory.CreateTempSubdirectory("stellar-package-").FullName;
        var staging = Path.Combine(cache, $".package-{Guid.NewGuid():N}");
        try
        {
            var zip = Path.Combine(work, "package.zip");
            await repository.DownloadModAsync(mod.DownloadUrl, zip, progress);
            await Task.Run(() => new ModInstallerService().InstallAsync(zip, staging));
            var package = ReadPackage(staging);
            if (package == null || !string.Equals(package.Mod.Id, id, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The downloaded package does not match {name}.");

            package.Mod.Version = version;
            if (package.Mod.Needs is not { Count: > 0 })
                package.Mod.Needs = needs;
            File.WriteAllText(Path.Combine(staging, "mod.json"), JsonSerializer.Serialize(package.Mod));
            try
            {
                Directory.Move(staging, destination);
            }
            catch (IOException) when (Directory.Exists(destination)) { }

            return ReadPackage(destination) ?? throw new InvalidDataException("The cached package has no metadata.");
        }
        finally
        {
            ProfileFiles.TryDeleteDirectory(staging);
            ProfileFiles.TryDeleteDirectory(work);
        }
    }
}
