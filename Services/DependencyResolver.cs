using StellarModManager.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StellarModManager.Services;

public static class DependencyResolver
{
    internal static (List<ModInfo> ToInstall, List<string> Missing) ResolveInstall(
        ModInfo root, IEnumerable<InstalledModInfo> installed, Func<string, ModInfo?> findAvailable)
    {
        var installedById = installed.ToDictionary(mod => mod.Id, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root.Id };
        var toInstall = new List<ModInfo>();
        var missing = new List<string>();
        Visit(root);
        return (toInstall, missing);

        void Visit(ModInfo mod)
        {
            foreach (var raw in NeedsOf(mod))
            {
                var id = raw?.Trim();
                if (string.IsNullOrEmpty(id) || !seen.Add(id))
                    continue;

                if (installedById.TryGetValue(id, out var existing))
                {
                    Visit(existing);
                    continue;
                }

                var available = findAvailable(id);
                if (available == null)
                {
                    missing.Add(id);
                    continue;
                }

                Visit(available);
                toInstall.Add(available);
            }
        }
    }

    private static List<string> NeedsOf(ModInfo mod)
    {
        if (mod.Needs is { Count: > 0 })
            return mod.Needs;

        return (mod as InstalledModInfo)?.Online?.Needs ?? new List<string>();
    }

    // Installed mods that have needs
    public static List<InstalledModInfo> FindDependents(InstalledModInfo target, IEnumerable<InstalledModInfo> installed)
    {
        return installed
            .Where(_mod => !_mod.Id.Equals(target.Id, StringComparison.OrdinalIgnoreCase))
            .Where(_mod => NeedsOf(_mod).Any(need => (need ?? "").Trim().Equals(target.Id, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
}
