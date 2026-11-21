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
    public ObservableCollection<InstalledModInfo> LibraryMods { get; } = new();
    public ObservableCollection<InstalledModInfo> AvailableModUpdates { get; } = new();
    private HashSet<string> managedModPaths = new(ProfileFiles.PathComparer);

    private InstalledModInfo? modPendingRemoval;
    private OnlineModInfo? modPendingInstall;

    [ObservableProperty] private bool isConfirmRemoveOpen;
    [ObservableProperty] private string confirmRemoveTitle = "";
    [ObservableProperty] private string confirmRemoveButton = "";
    [ObservableProperty] private string confirmRemoveMessage = "";
    [ObservableProperty] private bool isConfirmDependenciesOpen;
    [ObservableProperty] private string confirmDependenciesMessage = "";

    private void ShowInstalledMods(List<ProfileMod> mods)
    {
        var updatesId = UpdatesSelection?.Id;
        managedModPaths = mods.SelectMany(mod => mod.Files).ToHashSet(ProfileFiles.PathComparer);
        InstalledMods.Clear();
        foreach (var installed in mods)
            InstalledMods.Add(installed.Mod);
        ShowLibraryMods();
        RefreshUpdateStatuses(updatesId);
    }

    [RelayCommand]
    private void RefreshLibrary()
    {
        if (!IsModOperationInProgress)
            ShowLibraryMods();
    }

    private void ShowLibraryMods()
    {
        var selection = LibrarySelection;
        var external = profileStore == null ? new() : ExternalModScanner.Scan(GamePath, managedModPaths);
        LibraryMods.Clear();
        foreach (var mod in InstalledMods.Concat(external))
            LibraryMods.Add(mod);

        // Hmm...
        LibrarySelection = selection == null ? null : LibraryMods.FirstOrDefault(mod =>
            mod.IsExternal == selection.IsExternal && (mod.IsExternal
                ? ProfileFiles.PathComparer.Equals(mod.Id, selection.Id)
                : SameId(mod.Id, selection.Id)));
    }

    private static bool SameId(string id, string? other) =>
        string.Equals(id, other, StringComparison.OrdinalIgnoreCase);

    private void RefreshUpdateStatuses(string? previousSelection = null)
    {
        var updatesId = previousSelection ?? UpdatesSelection?.Id;
        foreach (var online in OnlineMods)
        {
            online.IsInstalled = InstalledMods.Any(m => SameId(m.Id, online.Id));
            online.HasUpdate = false;
        }

        foreach (var installed in InstalledMods)
        {
            installed.Online = OnlineMods.FirstOrDefault(m => SameId(m.Id, installed.Id));
            var online = installed.Online;
            var latest = online?.Version;
            var cached = packages.Find(installed.Id);
            if (cached != null && IsNewer(cached.Mod.Version, latest))
                latest = cached.Mod.Version;

            installed.IsUpdateAvailable = IsNewer(latest, installed.Version);
            installed.LatestVersion = installed.IsUpdateAvailable ? latest : null;
            installed.UpdateNotes = null;
            if (online != null && installed.IsUpdateAvailable)
            {
                online.HasUpdate = true;
                if (Version.TryParse(installed.Version, out var installedVersion))
                    _ = LoadUpdateNotesAsync(installed, online, installedVersion);
            }
        }

        RefreshDependencyStatuses();
        AvailableModUpdates.Clear();
        foreach (var installed in InstalledMods.Where(m => m.IsUpdateAvailable))
            AvailableModUpdates.Add(installed);
        UpdatesSelection = AvailableModUpdates.FirstOrDefault(m => SameId(m.Id, updatesId));
        ShowDetails();
    }

    private static bool IsNewer(string? candidate, string? current) =>
        Version.TryParse(candidate, out var candidateVersion) &&
        (!Version.TryParse(current, out var currentVersion) || candidateVersion > currentVersion);

    private async Task LoadUpdateNotesAsync(InstalledModInfo installed, OnlineModInfo online, Version installedVersion)
    {
        await repositoryService.LoadChangelogAsync(online);
        installed.UpdateNotes = string.Join("\n\n", online.Changelog
            .Where(e => e.Version > installedVersion)
            .Select(e => $"v{e.Version}\n{e.Notes}"));
    }

    private void RefreshDependencyStatuses()
    {
        var loc = LocalizationService.Instance;
        foreach (var installed in InstalledMods)
        {
            var (_, missing) = DependencyResolver.ResolveInstall(installed, InstalledMods, _ => null);
            var lines = missing.Select(id => loc.Format("DependencyMissingLine", DisplayNameFor(id)));
            installed.DependencyWarning = string.Join("\n", lines);
        }
    }

    private string DisplayNameFor(string id) => OnlineMods.FirstOrDefault(m => SameId(m.Id, id))?.Name ?? id;

    private ModInfo? FindAvailableMod(string id)
    {
        var online = OnlineMods.FirstOrDefault(m => SameId(m.Id, id));
        return (ModInfo?)online ?? packages.Find(id)?.Mod;
    }

    [RelayCommand]
    private Task InstallMod(OnlineModInfo mod) => InstallModAsync(mod, dependenciesConfirmed: false);

    private async Task InstallModAsync(OnlineModInfo mod, bool dependenciesConfirmed)
    {
        if (!TryBeginModOperation())
            return;
        try
        {
            var (dependencies, missing) = DependencyResolver.ResolveInstall(mod, InstalledMods, FindAvailableMod);
            if (missing.Count > 0)
            {
                ReportError("StatusDependencyNotInList", mod.Name, string.Join(", ", missing.Select(DisplayNameFor)));
                return;
            }

            var names = string.Join(", ", dependencies.Select(d => d.Name));
            if (dependencies.Count > 0 && !AutoDeployDependencies && !dependenciesConfirmed)
            {
                modPendingInstall = mod;
                ConfirmDependenciesMessage = LocalizationService.Instance.Format("ConfirmDependenciesMessage", mod.Name, names);
                IsConfirmDependenciesOpen = true;
                return;
            }

            var store = profileStore!;
            var active = ActiveProfile!.Id;
            var install = new List<ModPackage>();
            foreach (var dependency in dependencies)
                install.Add(await GetInstallPackage(dependency));
            install.Add(await GetInstallPackage(mod));

            mod.IsInstalling = true;
            ShowProfileState(await Task.Run(() => store.ChangeInstalledMods(active, install)));
            if (dependencies.Count == 0)
                ReportSuccess("StatusInstalled", mod.Name);
            else
                ReportSuccess("StatusInstalledWithDependencies", mod.Name, names);
        }
        catch (Exception ex)
        {
            ReportError("StatusInstallFailed", ex.Message);
        }
        finally
        {
            mod.IsInstalling = false;
            IsModOperationInProgress = false;
        }
    }

    private async Task<ModPackage> GetInstallPackage(ModInfo mod)
    {
        if (mod is not OnlineModInfo online)
        {
            return packages.Find(mod.Id, mod.Version)
                ?? throw new FileNotFoundException($"No cached package was found for {mod.Name}.");
        }

        online.IsInstalling = true;
        online.DownloadProgress = 0;
        try
        {
            var progress = new Progress<double>(pct => online.DownloadProgress = pct);
            return await packages.GetAsync(online, repositoryService, progress);
        }
        finally
        {
            online.IsInstalling = false;
        }
    }

    [RelayCommand]
    private async Task ConfirmDependencies()
    {
        var mod = modPendingInstall;
        CancelDependencies();
        if (mod != null)
            await InstallModAsync(mod, dependenciesConfirmed: true);
    }

    [RelayCommand]
    private void CancelDependencies()
    {
        IsConfirmDependenciesOpen = false;
        modPendingInstall = null;
    }

    [RelayCommand]
    private async Task RequestRemoveMod(InstalledModInfo mod)
    {
        if (mod.IsExternal || IsModOperationInProgress || IsConfirmRemoveOpen || IsConfirmDependenciesOpen)
            return;

        var dependents = DependencyResolver.FindDependents(mod, InstalledMods);
        if (dependents.Count > 0)
        {
            var names = string.Join(", ", dependents.Select(d => d.Name));
            modPendingRemoval = mod;
            ShowRemoveConfirmation("ConfirmRemoveTitle", "ConfirmRemoveButton", "ConfirmRemoveDependentsMessage", names, mod.Name);
        }
        else if (ConfirmBeforeRemove)
        {
            modPendingRemoval = mod;
            ShowRemoveConfirmation("ConfirmRemoveTitle", "ConfirmRemoveButton", "ConfirmRemoveMessage", mod.Name);
        }
        else
        {
            await UninstallMod(mod);
        }
    }

    private void ShowRemoveConfirmation(string title, string button, string message, params object[] args)
    {
        var loc = LocalizationService.Instance;
        ConfirmRemoveTitle = loc[title];
        ConfirmRemoveButton = loc[button];
        ConfirmRemoveMessage = loc.Format(message, args);
        IsConfirmRemoveOpen = true;
    }

    [RelayCommand]
    private async Task ConfirmRemove()
    {
        var profile = profilePendingDeletion;
        var mod = modPendingRemoval;
        CancelRemove();
        if (profile != null)
            await DeleteProfileAsync(profile);
        else if (mod != null)
            await UninstallMod(mod);
    }

    [RelayCommand]
    private void CancelRemove()
    {
        IsConfirmRemoveOpen = false;
        modPendingRemoval = null;
        profilePendingDeletion = null;
    }

    private async Task UninstallMod(InstalledModInfo mod)
    {
        if (!TryBeginModOperation())
            return;
        mod.IsRemoving = true;
        try
        {
            var store = profileStore!;
            var active = ActiveProfile!.Id;
            ShowProfileState(await Task.Run(() => store.ChangeInstalledMods(active, Array.Empty<ModPackage>(), mod.Id)));
            ReportSuccess("StatusRemoved", mod.Name);
        }
        catch (Exception ex)
        {
            ReportError("StatusRemoveFailed", ex.Message);
        }
        finally
        {
            mod.IsRemoving = false;
            IsModOperationInProgress = false;
        }
    }

    [RelayCommand]
    private async Task UpdateMod(InstalledModInfo mod)
    {
        if (!TryBeginModOperation())
            return;
        try
        {
            await UpdateInstalledMod(mod);
        }
        finally
        {
            IsModOperationInProgress = false;
        }
    }

    private async Task UpdateInstalledMod(InstalledModInfo mod)
    {
        var version = mod.LatestVersion;
        if (!mod.IsUpdateAvailable || version == null)
            return;

        mod.IsUpdating = true;
        mod.UpdateProgress = 0;
        try
        {
            var package = packages.Find(mod.Id, version);
            if (package == null)
            {
                var online = OnlineMods.FirstOrDefault(m => SameId(m.Id, mod.Id) && m.Version == version);
                if (online == null)
                {
                    ReportError("StatusNotInRepository");
                    return;
                }

                var progress = new Progress<double>(pct => mod.UpdateProgress = pct);
                package = await packages.GetAsync(online, repositoryService, progress);
            }

            var store = profileStore!;
            var active = ActiveProfile!.Id;
            ShowProfileState(await Task.Run(() => store.ChangeInstalledMods(active, new[] { package })));
            ReportSuccess("StatusUpdated", mod.Name, version);
        }
        catch (Exception ex)
        {
            ReportError("StatusUpdateFailed", ex.Message);
        }
        finally
        {
            mod.IsUpdating = false;
        }
    }

    [RelayCommand]
    private async Task UpdateAllMods()
    {
        if (!TryBeginModOperation())
            return;
        try
        {
            foreach (var id in AvailableModUpdates.Select(m => m.Id).ToList())
            {
                var mod = InstalledMods.FirstOrDefault(m => SameId(m.Id, id));
                if (mod != null)
                    await UpdateInstalledMod(mod);
            }
        }
        finally
        {
            IsModOperationInProgress = false;
        }
    }
}
