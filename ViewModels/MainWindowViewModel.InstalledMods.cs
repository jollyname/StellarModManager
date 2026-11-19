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
    public ObservableCollection<InstalledModInfo> InstalledMods { get; } = new();
    public ObservableCollection<InstalledModInfo> AvailableModUpdates { get; } = new();

    private InstalledModInfo? modPendingRemoval;

    [ObservableProperty]
    private bool isConfirmRemoveOpen;

    [ObservableProperty]
    private string confirmRemoveMessage = "";

    [ObservableProperty]
    private string confirmRemoveTitle = "";

    private void LoadInstalledMods(IEnumerable<InstalledModInfo> mods)
    {
        string? libraryId = LibrarySelection?.Id;
        InstalledMods.Clear();

        foreach (var mod in mods)
            InstalledMods.Add(mod);

        LibrarySelection = InstalledMods.FirstOrDefault(mod => mod.Id == libraryId);
        RefreshUpdateStatuses();
    }

    // Check for both id and version.
    private void RefreshUpdateStatuses()
    {
        string? updatesId = UpdatesSelection?.Id;

        var managed = InstalledMods.Where(mod => !mod.IsExternal).ToList();
        foreach (var online in OnlineMods)
        {
            online.IsInstalled = managed.Any(m => m.Id == online.Id);
            online.HasUpdate = false;
        }

        foreach (var installed in managed)
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

    private void RefreshDependencyStatuses()
    {
        var loc = LocalizationService.Instance;
        var managed = InstalledMods.Where(mod => !mod.IsExternal).ToList();
        foreach (var installed in managed)
        {
            var missing = DependencyResolver.FindMissing(installed, managed);
            installed.DependencyWarning = missing.Count == 0 ? "" :
                loc.Format("DependencyMissingLine", string.Join(", ", missing.Select(DisplayNameFor)));
        }
    }

    // Shows the name instead of id when the mod is in the online list
    private string DisplayNameFor(string id) => OnlineMods.FirstOrDefault(o => o.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name ?? id;

    private Task RemoveMod(InstalledModInfo mod) => RunProfileOperation("StatusRemoveFailed", async () =>
    {
        if (!InstalledMods.Contains(mod))
        {
            if (mod.IsExternal)
                ReportError("StatusExternalModChanged", mod.ExternalFile!.Path);
            return;
        }

        var profileId = ActiveProfile!.Id;
        var game = GamePath;
        mod.IsRemoving = true;

        try
        {
            var snapshot = await Task.Run(() => mod.ExternalFile is { } file
                ? profileStorage.RemoveExternal(profileId, game, file)
                : profileStorage.Remove(profileId, game, mod.Id));
            if (mod.ExternalFile is { } removed)
            {
                foreach (var row in InstalledMods.Where(row => row.ExternalFile?.Path == removed.Path).ToList())
                    InstalledMods.Remove(row);
            }

            ApplyProfileSnapshot(snapshot);
            ReportSuccess("StatusRemoved", mod.Name);
        }
        catch when (mod.ExternalFile != null)
        {
            changedExternalFiles.Add(mod.ExternalFile.Path);
            QueueExternalScan();
            throw;
        }
        finally
        {
            mod.IsRemoving = false;
        }
    });

    [RelayCommand]
    private async Task RequestRemoveMod(InstalledModInfo mod)
    {
        if (IsProfileBusy)
        {
            ReportError("StatusProfileBusy");
            return;
        }

        if (!InstalledMods.Contains(mod))
            return;

        if (mod.ExternalFile is { } file)
        {
            var localization = LocalizationService.Instance;
            profilePendingRemoval = null;
            modPendingRemoval = mod;
            ConfirmRemoveTitle = localization["ConfirmRemoveExternalTitle"];
            ConfirmRemoveMessage = localization.Format("ConfirmRemoveExternalMessage", file.Path);
            IsConfirmRemoveOpen = true;
            return;
        }

        var dependents = DependencyResolver.FindDependents(mod, InstalledMods.Where(item => !item.IsExternal));
        if (dependents.Count == 0 && !ConfirmBeforeRemove)
        {
            await RemoveMod(mod);
            return;
        }

        var loc = LocalizationService.Instance;
        profilePendingRemoval = null;
        modPendingRemoval = mod;
        ConfirmRemoveTitle = loc["ConfirmRemoveTitle"];
        ConfirmRemoveMessage = dependents.Count == 0
            ? loc.Format("ConfirmRemoveMessage", mod.Name)
            : loc.Format("ConfirmRemoveDependentsMessage", string.Join(", ", dependents.Select(dep => dep.Name)), mod.Name);
        IsConfirmRemoveOpen = true;
    }

    [RelayCommand]
    private async Task ConfirmRemove()
    {
        var profile = profilePendingRemoval;
        var mod = modPendingRemoval;
        CancelRemove();

        if (profile != null)
            await RemoveProfile(profile);
        else if (mod != null)
            await RemoveMod(mod);
    }

    [RelayCommand]
    private void CancelRemove()
    {
        IsConfirmRemoveOpen = false;
        modPendingRemoval = null;
        profilePendingRemoval = null;
    }

    [RelayCommand]
    private Task UpdateMod(InstalledModInfo mod) => RunProfileOperation("StatusUpdateFailed", () => UpdateActiveMod(mod));

    private async Task UpdateActiveMod(InstalledModInfo mod)
    {
        if (mod.IsExternal || !InstalledMods.Contains(mod))
            return;

        var profileId = ActiveProfile!.Id;
        var game = GamePath;
        var online = OnlineMods.FirstOrDefault(item => item.Id == mod.Id);
        if (online == null)
        {
            ReportError("StatusNotInRepository");
            return;
        }

        var plan = ResolveInstall(online);
        if (!CheckInstallDependencies(online, plan))
            return;

        mod.IsUpdating = true;
        mod.UpdateProgress = 0;
        try
        {
            var snapshot = await WithStagedModsAsync(plan.ToInstall,
                item => item == online ? new Progress<double>(pct => mod.UpdateProgress = pct) : null,
                staged => profileStorage.Install(profileId, game, staged));
            ApplyProfileSnapshot(snapshot);
            ReportSuccess("StatusUpdated", mod.Name, online.Version);
        }
        finally
        {
            mod.IsUpdating = false;
        }
    }

    [RelayCommand]
    private Task UpdateAllMods() => RunProfileOperation("StatusUpdateFailed", async () =>
    {
        foreach (var id in AvailableModUpdates.Select(mod => mod.Id).ToList())
        {
            var mod = InstalledMods.FirstOrDefault(item => item.Id == id);
            if (mod?.IsUpdateAvailable == true)
                await UpdateActiveMod(mod);
        }
    });
}
