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
    public ObservableCollection<InstalledModInfo> InstalledMods { get; } = new();
    public ObservableCollection<InstalledModInfo> AvailableModUpdates { get; } = new();
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
        string? libraryId = LibrarySelection?.Id;

        InstalledMods.Clear();

        var mods = installedModsService.GetInstalledMods(libraryPath);

        foreach (var mod in mods)
        {
            mod.IsDeployed = deploymentService.IsDeployed(Path.Combine(libraryPath, mod.Id));
            InstalledMods.Add(mod);
        }

        LibrarySelection = InstalledMods.FirstOrDefault(m => m.Id == libraryId);
        RefreshUpdateStatuses();
    }

    // Check for both id and version.
    private void RefreshUpdateStatuses()
    {
        string? updatesId = UpdatesSelection?.Id;

        foreach (var online in OnlineMods)
        {
            online.IsInstalled = InstalledMods.Any(m => m.Id == online.Id);
            online.HasUpdate = false;
        }

        foreach (var installed in InstalledMods)
        {
            var online = OnlineMods.FirstOrDefault(m => m.Id == installed.Id);
            installed.Online = online;

            if (online != null &&
                Version.TryParse(online.Version, out var onlineV) &&
                Version.TryParse(installed.Version, out var installedV) &&
                onlineV > installedV)
            {
                installed.IsUpdateAvailable = true;
                installed.LatestVersion = online.Version;
                online.HasUpdate = true;
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

        RefreshDependencyStatuses();

        AvailableModUpdates.Clear();

        foreach (var installed in InstalledMods.Where(m => m.IsUpdateAvailable))
            AvailableModUpdates.Add(installed);

        // Rebuilding the lists drops their selections; restore them by id
        UpdatesSelection = AvailableModUpdates.FirstOrDefault(m => m.Id == updatesId);
        ShowDetails();
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

    private InstalledModInfo? modPendingDeploy;
    private DeployPlan? pendingDeployPlan;

    [ObservableProperty] private bool isConfirmDeployOpen;
    [ObservableProperty] private string confirmDeployMessage = "";

    [RelayCommand]
    private async Task DeployMod(InstalledModInfo mod)
    {
        if (GamePath == "No game selected")
        {
            ReportError("StatusSelectGameFirst");
            return;
        }

        var plan = DependencyResolver.ResolveDeploy(mod, InstalledMods);

        if (plan.Missing.Count > 0)
        {
            ReportError("StatusDeployDependencyMissing", mod.Name, string.Join(", ", plan.Missing.Select(DisplayNameFor)));
            return;
        }

        if (plan.ToDeploy.Count == 0 || AutoDeployDependencies)
        {
            await DeployWithDependenciesAsync(mod, plan);
            return;
        }

        modPendingDeploy = mod;
        pendingDeployPlan = plan;
        ConfirmDeployMessage = LocalizationService.Instance.Format("ConfirmDeployMessage", mod.Name, string.Join(", ", plan.ToDeploy.Select(d => d.Name)));
        IsConfirmDeployOpen = true;
    }

    [RelayCommand]
    private async Task ConfirmDeploy()
    {
        IsConfirmDeployOpen = false;

        if (modPendingDeploy != null && pendingDeployPlan != null)
            await DeployWithDependenciesAsync(modPendingDeploy, pendingDeployPlan);

        modPendingDeploy = null;
        pendingDeployPlan = null;
    }

    [RelayCommand]
    private void CancelDeploy()
    {
        IsConfirmDeployOpen = false;
        modPendingDeploy = null;
        pendingDeployPlan = null;
    }

    private async Task DeployWithDependenciesAsync(InstalledModInfo mod, DeployPlan plan)
    {
        try
        {
            foreach (var dep in plan.ToDeploy)
                await DeployOneAsync(dep);

            await DeployOneAsync(mod);

            if (plan.ToDeploy.Count == 0)
                ReportSuccess("StatusDeployed", mod.Name);
            else
                ReportSuccess("StatusDeployedWithDependencies", mod.Name, string.Join(", ", plan.ToDeploy.Select(d => d.Name)));
        }
        catch (Exception ex)
        {
            ReportError("StatusDeployFailed", ex.Message);
        }
    }

    private async Task DeployOneAsync(InstalledModInfo mod)
    {
        mod.IsDeploying = true;
        mod.DeployProgress = 0;

        try
        {
            string libraryPath = Path.Combine(AppContext.BaseDirectory, "Library", mod.Id);
            var progress = new Progress<double>(pct => mod.DeployProgress = pct);

            await Task.Run(() => deploymentService.DeployMod(libraryPath, GamePath, progress));
            mod.IsDeployed = true;
            RefreshDependencyStatuses();
        }
        finally
        {
            mod.IsDeploying = false;
        }
    }

    private void RefreshDependencyStatuses()
    {
        var loc = LocalizationService.Instance;

        foreach (var installed in InstalledMods)
        {
            var plan = DependencyResolver.ResolveDeploy(installed, InstalledMods);
            var lines = new List<string>();

            if (plan.Missing.Count > 0)
                lines.Add(loc.Format("DependencyMissingLine", string.Join(", ", plan.Missing.Select(DisplayNameFor))));

            if (installed.IsDeployed && plan.ToDeploy.Count > 0)
                lines.Add(loc.Format("DependencyNotInGameLine", string.Join(", ", plan.ToDeploy.Select(d => d.Name))));

            installed.DependencyWarning = string.Join("\n", lines);
        }
    }

    // Shows the name instead of id when the mod is in the online list
    private string DisplayNameFor(string id) => OnlineMods.FirstOrDefault(o => o.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name ?? id;

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
            RefreshUpdateStatuses();
            ReportSuccess("StatusRemoved", mod.Name);
        }
        catch (Exception ex)
        {
            ReportError("StatusRemoveFailed", ex.Message);
        }
        finally
        {
            mod.IsRemoving = false;
        }
    }

    [RelayCommand]
    private void RequestRemoveMod(InstalledModInfo mod)
    {
        var dependents = DependencyResolver.FindDependents(mod, InstalledMods);

        // Always ask when other mods need this one, even if the user turned confirmations off!!!!
        if (dependents.Count > 0)
        {
            string names = string.Join(", ", dependents.Select(d => d.Name));

            modPendingRemoval = mod;
            ConfirmRemoveMessage = LocalizationService.Instance.Format("ConfirmRemoveDependentsMessage", names, mod.Name);
            IsConfirmRemoveOpen = true;
            return;
        }

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
            ReportError("StatusNotInRepository");
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
                ReportSuccess("StatusUpdated", mod.Name, onlineMatch.Version);
            }
            else
            {
                ReportSuccess("StatusUpdatedNotDeployed", mod.Name, onlineMatch.Version);
            }
        }
        catch (Exception ex)
        {
            ReportError("StatusUpdateFailed", ex.Message);
        }
        finally
        {
            mod.IsUpdating = false;
        }

        LoadInstalledMods();
    }

    [RelayCommand]
    private async Task UpdateAllMods()
    {
        // UpdateMod reloads the library, so look each mod up again by id
        foreach (var id in AvailableModUpdates.Select(m => m.Id).ToList())
        {
            var mod = InstalledMods.FirstOrDefault(m => m.Id == id);

            if (mod?.IsUpdateAvailable == true)
                await UpdateMod(mod);
        }
    }
}
