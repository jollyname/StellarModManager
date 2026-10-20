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

public sealed class DeployPlan
{
    // Installed but not deployed
    public List<InstalledModInfo> ToDeploy { get; } = new();

    // Needed ids that aren't installed at all
    public List<string> Missing { get; } = new();
}

public static class DependencyResolver
{
    public static DependencyResult Resolve(ModInfo root, IEnumerable<OnlineModInfo> available, Func<string, bool> isInstalled)
    {
        var result = new DependencyResult();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root.Id };
        var list = available.ToList();

        Visit(root);
        return result;

        void Visit(ModInfo mod)
        {
            foreach (string raw in mod.Needs ?? new())
            {
                string id = (raw ?? "").Trim();

                // Skip blanks, and anything already visited
                if (id.Length == 0 || !seen.Add(id))
                    continue;

                var found = list.FirstOrDefault(_mod => _mod.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

                if (found == null)
                {
                    if (!isInstalled(id))
                        result.Missing.Add(id);
                    continue;
                }

                Visit(found); // its own needs come first

                if (!isInstalled(id))
                    result.ToInstall.Add(found);
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

    public static DeployPlan ResolveDeploy(ModInfo root, IEnumerable<InstalledModInfo> installed)
    {
        var plan = new DeployPlan();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root.Id };
        var list = installed.ToList();

        Visit(root);
        return plan;

        void Visit(ModInfo mod)
        {
            foreach (string raw in NeedsOf(mod))
            {
                string id = (raw ?? "").Trim();

                // Skip blanks, and anything already visited
                if (id.Length == 0 || !seen.Add(id))
                    continue;

                var found = list.FirstOrDefault(_mod => _mod.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

                if (found == null)
                {
                    plan.Missing.Add(id);
                    continue;
                }

                Visit(found); // its own needs come first

                if (!found.IsDeployed)
                    plan.ToDeploy.Add(found);
            }
        }
    }
}