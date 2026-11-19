using StellarModManager.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StellarModManager.Services;

public sealed class DependencyResult
{
    // Mods to install
    public List<OnlineModInfo> ToInstall { get; } = new();

    // Needed ids that are neither installed nor in the mod list
    public List<string> Missing { get; } = new();
}

public static class DependencyResolver
{
    public static DependencyResult Resolve(IEnumerable<ModInfo> roots, IEnumerable<OnlineModInfo> available,
        Func<string, bool> isInstalled)
    {
        var result = new DependencyResult();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = available.ToDictionary(mod => mod.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
            Visit(root, include: true);

        return result;

        void Visit(ModInfo mod, bool include)
        {
            if (!seen.Add(mod.Id))
                return;

            foreach (var raw in NeedsOf(mod))
            {
                string id = (raw ?? "").Trim();
                if (id.Length == 0 || seen.Contains(id))
                    continue;

                if (list.TryGetValue(id, out var found))
                    Visit(found, include: !isInstalled(id));
                else if (!isInstalled(id) && seen.Add(id))
                    result.Missing.Add(id);
            }

            if (include && mod is OnlineModInfo online)
                result.ToInstall.Add(online);
        }
    }

    public static List<string> FindMissing(InstalledModInfo root, IEnumerable<InstalledModInfo> installed)
    {
        var list = installed.ToDictionary(mod => mod.Id, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root.Id };
        var missing = new List<string>();
        Visit(root);
        return missing;

        void Visit(ModInfo mod)
        {
            foreach (var raw in NeedsOf(mod))
            {
                var id = (raw ?? "").Trim();
                if (id.Length == 0 || !seen.Add(id))
                    continue;

                if (list.TryGetValue(id, out var found))
                    Visit(found);
                else
                    missing.Add(id);
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