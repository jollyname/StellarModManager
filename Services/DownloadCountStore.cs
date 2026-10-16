using StellarModManager.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace StellarModManager.Services;

public sealed class DownloadCountStore
{
    private readonly string path;
    private readonly object gate = new();
    private readonly Dictionary<string, SavedCounts> mods;

    public DownloadCountStore(string path)
    {
        this.path = path;
        mods = Load();
    }

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "Data", "github-stats.json");

    public void Apply(OnlineModInfo mod)
    {
        if (mod.Changelog.Count > 0)
            return;

        lock (gate)
        {
            if (!mods.TryGetValue(Key(mod), out SavedCounts? saved))
                return;

            mod.Downloads = saved.Downloads;
            mod.LastUpdated = saved.LastUpdated;

            foreach (SavedNote note in saved.Notes ?? new())
            {
                if (Version.TryParse(note.Version, out Version? version))
                    mod.Changelog.Add(new ChangelogEntry(version, note.Notes ?? ""));
            }
        }
    }

    public void Remember(OnlineModInfo mod)
    {
        var saved = new SavedCounts
        {
            Downloads = mod.Downloads,
            LastUpdated = mod.LastUpdated,
            Notes = mod.Changelog.Select(entry => new SavedNote
            {
                Version = entry.Version.ToString(),
                Notes = entry.Notes
            }).ToList()
        };

        lock (gate)
        {
            mods[Key(mod)] = saved;
            string? folder = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);

            File.WriteAllText(path, JsonSerializer.Serialize(new SavedFile { Mods = mods }));
        }
    }

    private Dictionary<string, SavedCounts> Load()
    {
        try
        {
            if (!File.Exists(path))
                return new(StringComparer.OrdinalIgnoreCase);

            SavedFile? file = JsonSerializer.Deserialize<SavedFile>(File.ReadAllText(path));

            if (file?.Mods == null)
                return new(StringComparer.OrdinalIgnoreCase);

            return new Dictionary<string, SavedCounts>(file.Mods, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string Key(OnlineModInfo mod) => mod.RepoOwner + "/" + mod.RepoName;
}

public sealed class SavedFile
{
    public Dictionary<string, SavedCounts> Mods { get; set; } = new();
}

public sealed class SavedCounts
{
    public long Downloads { get; set; }

    public DateTime? LastUpdated { get; set; }

    public List<SavedNote> Notes { get; set; } = new();
}

public sealed class SavedNote
{
    public string Version { get; set; } = "";

    public string Notes { get; set; } = "";
}
