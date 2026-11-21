using StellarModManager.Models;
using System;
using System.Collections.Generic;

namespace StellarModManager.Services;

public sealed class ModListResult
{
    private readonly object _gate = new();

    public List<OnlineModInfo> Mods { get; } = new();

    public HashSet<string> FailedRepositories { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void AddFailure(string author, string repo)
    {
        lock (_gate)
            FailedRepositories.Add(author + "/" + repo);
    }

    public bool Failed(string author, string repo)
    {
        lock (_gate)
            return FailedRepositories.Contains(author + "/" + repo);
    }
}
