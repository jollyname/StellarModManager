using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarModManager.Models;
using StellarModManager.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace StellarModManager.ViewModels;

public partial class MainWindowViewModel
{
    public ObservableCollection<InstalledModInfo> InstalledMods { get; } = new();
    private readonly InstalledModsService installedModsService = new();
    private readonly ModDeploymentService deploymentService = new();

    private InstalledModInfo? modPendingRemoval;

    [ObservableProperty]
    private bool isConfirmRemoveOpen;

    [ObservableProperty]
    private string confirmRemoveMessage = "";

    private void LoadInstalledMods()
    {
        string libraryPath = Path.Combine(AppContext.BaseDirectory, "Library");

        InstalledMods.Clear();

        var mods = installedModsService.GetInstalledMods(libraryPath);

        foreach (var mod in mods)
        {
            InstalledMods.Add(mod);
        }

        RefreshUpdateStatuses();
    }

    // Check for both id and version.
    private void RefreshUpdateStatuses()
    {
        foreach (var installed in InstalledMods)
        {
            var online = OnlineMods.FirstOrDefault(m => m.Id == installed.Id);

            if (online != null &&
                Version.TryParse(online.Version, out var onlineV) &&
                Version.TryParse(installed.Version, out var installedV) &&
                onlineV > installedV)
            {
                installed.IsUpdateAvailable = true;
                installed.LatestVersion = online.Version;
                // load changelog
                _ = LoadUpdateNotesAsync(installed, online, installedV);
            }
            else
            {
                installed.IsUpdateAvailable = false;
                installed.LatestVersion = null;
                installed.UpdateNotes = null;
            }
        }
    }

    private async Task LoadUpdateNotesAsync(InstalledModInfo installed, OnlineModInfo online, Version installedV)
    {
        await repositoryService.LoadChangelogAsync(online);
        // show all version logs not just previous
        installed.UpdateNotes = string.Join("\n\n", online.Changelog
            .Where(e => e.Version > installedV)
            .Select(e => $"v{e.Version}\n{e.Notes}"));
    }

    // no longer wipes install, it deploys mods on update
    private async Task<bool> InstallToLibraryAsync(string zipFile, string libraryPath)
    {
        bool wasDeployed = GamePath != "No game selected" && deploymentService.IsDeployed(libraryPath);

        if (wasDeployed)
            await Task.Run(() => deploymentService.RemoveDeployedFiles(libraryPath, GamePath));

        await installerService.InstallAsync(zipFile, libraryPath);

        if (wasDeployed)
            await Task.Run(() => deploymentService.DeployMod(libraryPath, GamePath));

        return wasDeployed;
    }

    [RelayCommand]
    private async Task DeployMod(InstalledModInfo mod)
    {
        if (GamePath == "No game selected")
        {
            MelonLoaderStatusText = "Select a game first";
            return;
        }

        mod.IsDeploying = true;
        mod.DeployProgress = 0;

        try
        {
            string libraryPath = Path.Combine(AppContext.BaseDirectory, "Library", mod.Id);
            var progress = new Progress<double>(pct => mod.DeployProgress = pct);

            await Task.Run(() => deploymentService.DeployMod(libraryPath, GamePath, progress));

            MelonLoaderStatusText = $"{mod.Name} copied to game";
        }
        catch (Exception ex)
        {
            MelonLoaderStatusText = $"Deploy failed: {ex.Message}";
        }
        finally
        {
            mod.IsDeploying = false;
        }
    }

    [RelayCommand]
    private async Task RemoveMod(InstalledModInfo mod)
    {
        mod.IsRemoving = true;

        try
        {
            string libraryPath = Path.Combine(AppContext.BaseDirectory, "Library", mod.Id);

            await Task.Run(() =>
            {
                if (GamePath != "No game selected")
                {
                    deploymentService.RemoveDeployedFiles(libraryPath, GamePath);
                }

                if (Directory.Exists(libraryPath))
                {
                    Directory.Delete(libraryPath, true);
                }
            });

            InstalledMods.Remove(mod);
            MelonLoaderStatusText = $"{mod.Name} removed";
        }
        catch (Exception ex)
        {
            MelonLoaderStatusText = $"Remove failed: {ex.Message}";
        }
        finally
        {
            mod.IsRemoving = false;
        }
    }

    [RelayCommand]
    private void RequestRemoveMod(InstalledModInfo mod)
    {
        if (ConfirmBeforeRemove)
        {
            modPendingRemoval = mod;
            ConfirmRemoveMessage = LocalizationService.Instance.Format("ConfirmRemoveMessage", mod.Name);
            IsConfirmRemoveOpen = true;
        }
        else
        {
            _ = RemoveMod(mod);
        }
    }

    [RelayCommand]
    private async Task ConfirmRemove()
    {
        IsConfirmRemoveOpen = false;

        if (modPendingRemoval != null)
        {
            await RemoveMod(modPendingRemoval);
            modPendingRemoval = null;
        }
    }

    [RelayCommand]
    private void CancelRemove()
    {
        IsConfirmRemoveOpen = false;
        modPendingRemoval = null;
    }

    [RelayCommand]
    private async Task UpdateMod(InstalledModInfo mod)
    {
        OnlineModInfo? onlineMatch = OnlineMods.FirstOrDefault(m => m.Id == mod.Id);

        if (onlineMatch == null)
        {
            MelonLoaderStatusText = "Could not find this mod in the online repository";
            return;
        }

        mod.IsUpdating = true;
        mod.UpdateProgress = 0;
         
        try
        {
            string libraryPath = Path.Combine(AppContext.BaseDirectory, "Library", mod.Id);

            string downloads = Path.Combine(AppContext.BaseDirectory, "Downloads");
            Directory.CreateDirectory(downloads);

            string zipFile = Path.Combine(downloads, $"{mod.Id}.zip");
            var progress = new Progress<double>(pct => mod.UpdateProgress = pct);

            await repositoryService.DownloadModAsync(onlineMatch.DownloadUrl, zipFile, progress);
            bool redeployed = await InstallToLibraryAsync(zipFile, libraryPath);

            File.Delete(zipFile);

            if (redeployed)
            {
                MelonLoaderStatusText = $"{mod.Name} updated to {onlineMatch.Version}";
            }
            else
            {
                MelonLoaderStatusText = $"{mod.Name} updated to {onlineMatch.Version}. Install it to your game to use it.";
            }
        }
        catch (Exception ex)
        {
            MelonLoaderStatusText = $"Update failed: {ex.Message}";
        }
        finally
        {
            mod.IsUpdating = false;
        }

        LoadInstalledMods();
    }
}