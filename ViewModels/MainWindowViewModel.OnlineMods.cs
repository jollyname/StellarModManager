using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarModManager.Models;
using StellarModManager.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace StellarModManager.ViewModels;

public partial class MainWindowViewModel
{
    public ObservableCollection<OnlineModInfo> OnlineMods { get; } = new();
    // OnlineMods filtered by SearchText and sorted by SortIndex
    public ObservableCollection<OnlineModInfo> VisibleOnlineMods { get; } = new();
    private readonly ModRepositoryService repositoryService = new();
    private readonly ModInstallerService installerService = new();

    [ObservableProperty]
    private int sortIndex;

    [ObservableProperty]
    private string searchText = "";

    [RelayCommand]
    private Task InstallMod(OnlineModInfo mod) => RunProfileOperation("StatusInstallFailed", async () =>
    {
        if (ActiveProfile is not { } profile)
        {
            ReportError("StatusSelectGameFirst");
            return;
        }

        var profileId = profile.Id;
        var game = GamePath;
        var plan = ResolveInstall(mod);
        if (!CheckInstallDependencies(mod, plan))
            return;

        mod.IsInstalling = true;
        mod.DownloadProgress = 0;
        try
        {
            var snapshot = await WithStagedModsAsync(plan.ToInstall, item =>
                new Progress<double>(pct => item.DownloadProgress = pct),
                staged => profileStorage.Install(profileId, game, staged));
            ApplyProfileSnapshot(snapshot);
            var dependencies = plan.ToInstall.Where(item => item != mod).Select(item => item.Name).ToList();
            if (dependencies.Count == 0)
                ReportSuccess("StatusInstalled", mod.Name);
            else
                ReportSuccess("StatusInstalledWithDependencies", mod.Name, string.Join(", ", dependencies));
        }
        finally
        {
            mod.IsInstalling = false;
        }
    });

    private DependencyResult ResolveInstall(OnlineModInfo mod) =>
        DependencyResolver.Resolve([mod], OnlineMods,
            id => InstalledMods.Any(item => !item.IsExternal && item.Id.Equals(id, StringComparison.OrdinalIgnoreCase)));

    private bool CheckInstallDependencies(OnlineModInfo mod, DependencyResult plan)
    {
        if (plan.Missing.Count == 0)
            return true;

        ReportError("StatusDependencyNotInList", mod.Name, string.Join(", ", plan.Missing));
        return false;
    }

    private async Task<ProfileSnapshot> WithStagedModsAsync(IReadOnlyList<OnlineModInfo> mods,
        Func<OnlineModInfo, IProgress<double>?> progress, Func<IReadOnlyList<StagedMod>, ProfileSnapshot> apply)
    {
        var staging = Directory.CreateTempSubdirectory("StellarModManager-");
        try
        {
            var staged = new List<StagedMod>();
            foreach (var mod in mods)
            {
                var id = mod.Id;
                var url = mod.DownloadUrl;
                var packagePath = Path.Combine(staging.FullName, Guid.NewGuid().ToString("N"));
                var zip = packagePath + ".zip";
                await repositoryService.DownloadModAsync(url, zip, progress(mod));
                var package = await Task.Run(() => installerService.Extract(zip, packagePath, id));
                if (package.Package.Mod.Needs is not { Count: > 0 })
                    package.Package.Mod.Needs = mod.Needs?.ToList() ?? [];

                try
                {
                    await Task.Run(() => releaseFingerprints.Record(url, package));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException){}
                staged.Add(package);
                File.Delete(zip);
            }

            return await Task.Run(() => apply(staged));
        }
        finally
        {
            try
            {
                staging.Delete(recursive: true);
            }
            catch (IOException){}
            catch (UnauthorizedAccessException){}
        }
    }

    [RelayCommand]
    private async Task RefreshMods()
    {
        ModListResult loaded;

        try
        {
            loaded = await repositoryService.GetModsAsync(
                "https://jollyname.dev/mods.json"
            );
        }
        // Catch failed refresh
        catch (Exception ex)
        {
            ReportError("StatusRefreshFailed", ex.Message);
            return;
        }

        List<OnlineModInfo> mods = loaded.Mods;

        // A repository that failed to answer keeps the card already on screen.
        foreach (var gone in OnlineMods.Where(o => mods.All(m => m.Id != o.Id)).ToList())
        {
            if (loaded.Failed(gone.RepoOwner, gone.RepoName))
                continue;

            OnlineMods.Remove(gone);
        }

        foreach (var mod in mods)
        {
            var existing = OnlineMods.FirstOrDefault(x => x.Id == mod.Id);

            if (existing != null)
            {
                // version changes reset images and changelog
                bool versionChanged = existing.Version != mod.Version;

                existing.Name = mod.Name;
                existing.Author = mod.Author;
                existing.Ai = mod.Ai;
                existing.Development = mod.Development;
                existing.Tags = mod.Tags;
                existing.RebuildTags();
                existing.Version = mod.Version;
                existing.Description = mod.Description;
                existing.Needs = mod.Needs;
                existing.DownloadUrl = mod.DownloadUrl;
                existing.RepoOwner = mod.RepoOwner;
                existing.RepoName = mod.RepoName;

                if (versionChanged || existing.ThumbnailUrl != mod.ThumbnailUrl)
                {
                    existing.ThumbnailUrl = mod.ThumbnailUrl;
                    existing.ThumbnailImage = null;
                    existing.GalleryImages.Clear();
                    existing.GalleryLoaded = false;
                }

                if (versionChanged || !existing.ImageUrls.SequenceEqual(mod.ImageUrls))
                {
                    existing.ImageUrls = mod.ImageUrls;
                    existing.GalleryImages.Clear();
                    existing.GalleryLoaded = false;
                }

                if (versionChanged)
                {
                    existing.Changelog.Clear();
                    existing.ChangelogTask = null;
                }
            }
            else
            {
                mod.RebuildTags();
                OnlineMods.Add(mod);
            }
        }

        foreach (var mod in OnlineMods)
            _ = repositoryService.LoadThumbnailAsync(mod);


        RefreshUpdateStatuses();
        QueueExternalScan();
        ApplyOnlineView();

        await Task.WhenAll(OnlineMods.Select(repositoryService.LoadChangelogAsync));
        ApplyOnlineView();
    }

    partial void OnSortIndexChanged(int value)
    {
        ApplyOnlineView();
        PersistSettings();
    }

    partial void OnSearchTextChanged(string value) => ApplyOnlineView();

    private void ApplyOnlineView()
    {
        var filtered = OnlineMods.Where(m =>
            string.IsNullOrWhiteSpace(SearchText) ||
            m.Name.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ||
            m.Author.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase));

        var sorted = SortIndex switch
        {
            1 => filtered.OrderByDescending(m => m.LastUpdated).ToList(),
            2 => filtered.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            _ => filtered.OrderByDescending(m => m.Downloads).ToList()
        };

        // Update in place so the list keeps its selection and scroll position
        foreach (var gone in VisibleOnlineMods.Except(sorted).ToList())
            VisibleOnlineMods.Remove(gone);

        for (int i = 0; i < sorted.Count; i++)
        {
            int current = VisibleOnlineMods.IndexOf(sorted[i]);

            if (current == -1)
                VisibleOnlineMods.Insert(i, sorted[i]);
            else if (current != i)
                VisibleOnlineMods.Move(current, i);
        }
    }
}
