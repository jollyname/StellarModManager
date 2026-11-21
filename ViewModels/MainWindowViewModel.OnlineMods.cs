using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarModManager.Models;
using StellarModManager.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace StellarModManager.ViewModels;

public partial class MainWindowViewModel
{
    public ObservableCollection<OnlineModInfo> OnlineMods { get; } = [];
    // OnlineMods filtered by SearchText and sorted by SortIndex
    public ObservableCollection<OnlineModInfo> VisibleOnlineMods { get; } = [];
    private readonly ModRepositoryService _repositoryService = new();

    [ObservableProperty]
    public partial int SortIndex { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [RelayCommand]
    private async Task RefreshMods()
    {
        ModListResult loaded;

        try
        {
            loaded = await _repositoryService.GetModsAsync(
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
            _ = _repositoryService.LoadThumbnailAsync(mod);


        RefreshUpdateStatuses();
        ApplyOnlineView();

        await Task.WhenAll(OnlineMods.Select(_repositoryService.LoadChangelogAsync));
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
