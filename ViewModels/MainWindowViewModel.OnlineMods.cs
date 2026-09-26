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
    private readonly ModRepositoryService repositoryService = new();
    private readonly ModInstallerService installerService = new();

    [RelayCommand]
    private async Task InstallMod(OnlineModInfo mod)
    {
        mod.IsInstalling = true;
        mod.DownloadProgress = 0;

        try
        {
            string downloads = Path.Combine(AppContext.BaseDirectory, "Downloads");
            Directory.CreateDirectory(downloads);

            string zipFile = Path.Combine(downloads, $"{mod.Id}.zip");
            var progress = new Progress<double>(pct => mod.DownloadProgress = pct);

            await repositoryService.DownloadModAsync(mod.DownloadUrl, zipFile, progress);

            string libraryPath = Path.Combine(AppContext.BaseDirectory, "Library", mod.Id);
            await InstallToLibraryAsync(zipFile, libraryPath);

            mod.IsInstalled = true;

            File.Delete(zipFile);

            MelonLoaderStatusText = $"{mod.Name} installed";
        }
        catch (Exception ex)
        {
            MelonLoaderStatusText = $"Install failed: {ex.Message}";
        }
        finally
        {
            mod.IsInstalling = false;
        }

        // refresh no longer needed
        LoadInstalledMods();
    }

    [RelayCommand]
    private async Task RefreshMods()
    {
        List<OnlineModInfo> mods;

        try
        {
            mods = await repositoryService.GetModsAsync(
                "https://raw.githubusercontent.com/jollyname/StellarModRepository/main/mods.json"
            );
        }
        // catch failed refresh
        catch (Exception ex)
        {
            MelonLoaderStatusText = $"Refresh failed: {ex.Message}";
            return;
        }
        // remove old mods when finding mods
        foreach (var gone in OnlineMods.Where(o => mods.All(m => m.Id != o.Id)).ToList())
            OnlineMods.Remove(gone);

        foreach (var mod in mods)
        {
            mod.IsInstalled = Directory.Exists(Path.Combine(AppContext.BaseDirectory, "Library", mod.Id));

            var existing = OnlineMods.FirstOrDefault(x => x.Id == mod.Id);

            if (existing != null)
            {
                // version changes reset images and changelog
                bool versionChanged = existing.Version != mod.Version;

                existing.Name = mod.Name;
                existing.Author = mod.Author;
                existing.Version = mod.Version;
                existing.Description = mod.Description;
                existing.DownloadUrl = mod.DownloadUrl;
                existing.RepoOwner = mod.RepoOwner;
                existing.RepoName = mod.RepoName;
                existing.IsInstalled = mod.IsInstalled;

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
                OnlineMods.Add(mod);
            }
        }

        foreach (var mod in OnlineMods)
            _ = repositoryService.LoadThumbnailAsync(mod);


        RefreshUpdateStatuses();
    }
}