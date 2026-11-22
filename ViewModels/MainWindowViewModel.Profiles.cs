using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarModManager.Models;
using StellarModManager.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace StellarModManager.ViewModels;

// :)

public partial class MainWindowViewModel
{
    private sealed record ProfileFile(string Name, List<string> DeployedModIds);

    private readonly ModPackageCache _packages = new(AppContext.BaseDirectory);
    private ModProfileStore? _profileStore;
    private ModProfile? _profilePendingDeletion;

    public ObservableCollection<ModProfile> ModProfiles { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveProfileName))]
    public partial ModProfile? ActiveProfile { get; set; }

    public string ActiveProfileName => ActiveProfile?.Name ?? "Profile";

    [ObservableProperty]
    public partial bool IsProfilesDropdownOpen { get; set; }

    [ObservableProperty]
    public partial string NewProfileName { get; set; } = "";

    [ObservableProperty]
    public partial bool IsModOperationInProgress { get; set; }


    private void OpenGameProfiles(string gamePath, string? legacyGamePath)
    {
        try
        {
            var store = new ModProfileStore(AppContext.BaseDirectory, gamePath, _packages);
            var state = store.Open(legacyGamePath);
            _profileStore = store;
            ShowProfileState(state);
        }
        catch (Exception ex)
        {
            ClearGameProfiles();
            ReportError("StatusProfileOpenFailed", ex.Message);
        }
    }

    private void ClearGameProfiles()
    {
        _profileStore = null;
        ActiveProfile = null;
        ModProfiles.Clear();
        ShowInstalledMods([]);
    }

    private void ShowProfileState(GameModProfiles state)
    {
        ModProfiles.Clear();
        foreach (var profile in state.Profiles)
        {
            profile.IsActive = profile.Id == state.ActiveProfileId;
            ModProfiles.Add(profile);
        }

        ActiveProfile = ModProfiles.Single(p => p.IsActive);
        ShowInstalledMods(state.InstalledMods);
    }

    private bool TryBeginModOperation(bool requireProfile = true)
    {
        if (IsModOperationInProgress || IsInstallingMelonLoader || IsConfirmRemoveOpen || IsConfirmDependenciesOpen)
        {
            ReportError("StatusModOperationBusy");
            return false;
        }

        if (requireProfile && (_profileStore == null || ActiveProfile == null))
        {
            ReportError("StatusSelectGameFirst");
            return false;
        }

        IsModOperationInProgress = true;
        return true;
    }

    private async Task<(List<string> Ids, List<string> Unavailable)> EnsureProfilePackagesAsync(IEnumerable<string> requested, bool followNeeds)
    {
        var ids = new List<string>();
        var unavailable = new List<string>();
        var queue = new Queue<string>(requested);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // We NEED the mod list
        if (OnlineMods.Count == 0)
            await RefreshMods();

        while (queue.TryDequeue(out var id))
        {
            if (!seen.Add(id))
                continue;
            ids.Add(id);

            var package = _packages.Find(id);
            if (package == null)
            {
                var online = OnlineMods.FirstOrDefault(m => SameId(m.Id, id));
                if (online == null)
                {
                    unavailable.Add(id);
                    continue;
                }
                package = await GetInstallPackage(online);
            }

            // What this mod needs has to be in the profile too
            if (followNeeds)
            {
                foreach (var need in package.Mod.Needs ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(need))
                        queue.Enqueue(need.Trim());
                }
            }
        }

        // We return unavailable to tell users which mods couldn't be installed
        return (ids, unavailable);
    }

    [RelayCommand]
    private void ToggleProfilesDropdown() => IsProfilesDropdownOpen = !IsProfilesDropdownOpen;

    [RelayCommand]
    private Task SwitchProfile(ModProfile? profile)
    {
        if (profile == null || profile.Id == ActiveProfile?.Id)
            return Task.CompletedTask;
        return ApplyProfileSwitch(profile.Id);
    }

    [RelayCommand]
    private async Task CreateProfile()
    {
        var name = NewProfileName.Trim();
        if (name.Length == 0)
            return;
        if (ModProfiles.Any(p => p.Name == name))
        {
            ReportError("StatusProfileNameTaken");
            return;
        }

        if (await ApplyProfileSwitch(Guid.Empty, clone: new ModProfile { Name = name }))
            NewProfileName = "";
    }

    [RelayCommand]
    private async Task DeleteProfile(ModProfile? profile)
    {
        if (profile == null || ModProfiles.Count <= 1 || IsModOperationInProgress ||
            IsConfirmRemoveOpen || IsConfirmDependenciesOpen)
            return;

        if (profile.Id == ActiveProfile?.Id || profile.SnapshotId.HasValue)
        {
            _profilePendingDeletion = profile;
            ShowRemoveConfirmation("ProfileDeleteTitle", "ProfileDelete", "ProfileDeleteMessage", profile.Name);
            return;
        }
        await DeleteProfileAsync(profile);
    }

    private Task DeleteProfileAsync(ModProfile profile)
    {
        if (ModProfiles.Count <= 1)
            return Task.CompletedTask;
        if (profile.Id == ActiveProfile?.Id)
            return ApplyProfileSwitch(ModProfiles.First(p => p.Id != profile.Id).Id, deleteActive: true);

        return EditProfiles(state => state.Profiles.RemoveAll(p => p.Id == profile.Id), "StatusProfileDeleteFailed");
    }

    private async Task<bool> ApplyProfileSwitch(Guid target, ModProfile? clone = null, bool deleteActive = false)
    {
        if (IsGameRunning)
        {
            ReportError("StatusProfileGameRunning");
            return false;
        }

        if (!TryBeginModOperation())
            return false;
        try
        {
            var store = _profileStore!;
            var active = ActiveProfile!.Id;
            var gamePath = GamePath;

            var (state, missing) = await Task.Run(() => store.Switch(active, target, clone, deleteActive, CheckGameStopped));
            ShowProfileState(state);
            if (missing.Count > 0)
                ReportError("StatusProfileMissingPackages", string.Join(", ", missing));
            else
                ReportSuccess("StatusProfileSwitched", ActiveProfileName);
            return true;

            void CheckGameStopped()
            {
                var executable = new FileInfo(Path.Combine(gamePath, GameLocatorService.ExecutableName));
                using var running = executable.Exists ? GameLaunchService.DetectExisting(executable) : null;
                if (running != null)
                    throw new InvalidOperationException(LocalizationService.Instance["StatusProfileGameRunning"]);
            }
        }
        catch (Exception ex)
        {
            ReportError("StatusProfileSwitchFailed", ex.Message);
            return false;
        }
        finally
        {
            IsModOperationInProgress = false;
        }
    }

    [RelayCommand]
    private void StartRename(ModProfile? profile)
    {
        if (profile == null || IsModOperationInProgress)
            return;
        profile.EditName = profile.Name;
        profile.IsEditing = true;
    }

    [RelayCommand]
    private async Task CommitRename(ModProfile? profile)
    {
        if (profile == null)
            return;
        var name = profile.EditName.Trim();
        profile.IsEditing = false;
        if (name.Length == 0 || name == profile.Name)
            return;

        await EditProfiles(state =>
        {
            EnsureUniqueProfileName(state, name, profile.Id);
            state.Profiles.Single(p => p.Id == profile.Id).Name = name;
        }, "StatusProfileRenameFailed");
    }

    [RelayCommand]
    private static void CancelRename(ModProfile? profile)
    {
        profile?.IsEditing = false;
    }

    private async Task EditProfiles(Action<GameModProfiles> edit, string errorKey)
    {
        if (!TryBeginModOperation())
            return;
        try
        {
            var store = _profileStore!;
            var active = ActiveProfile!.Id;
            ShowProfileState(await Task.Run(() => store.Edit(active, edit)));
        }
        catch (Exception ex)
        {
            ReportError(errorKey, ex.Message);
        }
        finally
        {
            IsModOperationInProgress = false;
        }
    }

    private static void EnsureUniqueProfileName(GameModProfiles state, string name, Guid? except = null)
    {
        if (state.Profiles.Any(p => p.Name == name && p.Id != except))
            throw new InvalidOperationException(LocalizationService.Instance["StatusProfileNameTaken"]);
    }

    private static Window? GetMainWindow() =>
        (App.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    [RelayCommand]
    private async Task ExportProfile()
    {
        if (ActiveProfile == null || GetMainWindow() is not Window window || !TryBeginModOperation())
            return;

        try
        {
            var profile = new ProfileFile(ActiveProfileName, InstalledMods.Select(m => m.Id).ToList());
            var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = LocalizationService.Instance["ProfileExport"],
                SuggestedFileName = profile.Name,
                DefaultExtension = "json",
                FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
            });

            if (file == null)
                return;
            await using var stream = await file.OpenWriteAsync();
            if (stream.CanSeek)
                stream.SetLength(0);
            await JsonSerializer.SerializeAsync(stream, profile, new JsonSerializerOptions { WriteIndented = true });
            ReportSuccess("StatusProfileExported", profile.Name);
        }
        catch (Exception ex)
        {
            ReportError("StatusProfileExportFailed", ex.Message);
        }
        finally
        {
            IsModOperationInProgress = false;
        }
    }

    [RelayCommand]
    private async Task ImportProfile()
    {
        if (GetMainWindow() is not Window window || !TryBeginModOperation())
            return;

        try
        {
            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = LocalizationService.Instance["ProfileImport"],
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
            });

            if (files.Count == 0)
                return;

            await using var stream = await files[0].OpenReadAsync();
            var imported = await JsonSerializer.DeserializeAsync<ProfileFile>(stream);
            if (string.IsNullOrWhiteSpace(imported?.Name))
                throw new InvalidDataException(LocalizationService.Instance["StatusProfileInvalid"]);

            var profileName = imported.Name.Trim();

            // Find the requested mods
            var requested = (imported.DeployedModIds ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            // Make sure there aren't over a hundred, otherwise it'll count as an invalid profile
            if (requested.Count > 100)
                throw new InvalidDataException(LocalizationService.Instance["StatusProfileInvalid"]);

            // Download what isn't cached yet, plus whatever those mods need
            var (ids, unavailable) = await EnsureProfilePackagesAsync(requested, followNeeds: true);

            var profile = new ModProfile { Name = profileName, DeployedModIds = ids };
            var store = _profileStore!;
            var active = ActiveProfile!.Id;
            var state = await Task.Run(() => store.Edit(active, current =>
            {
                EnsureUniqueProfileName(current, profile.Name);
                current.Profiles.Add(profile);
            }));
            ShowProfileState(state);

            // Tell the user about mods that aren't on installed, or on SM
            if (unavailable.Count > 0)
                ReportError("StatusProfileImportedMissing", profileName, string.Join(", ", unavailable));
            else
                ReportSuccess("StatusProfileImported", profileName);
        }
        catch (Exception ex)
        {
            ReportError("StatusProfileImportFailed", ex.Message);
        }
        finally
        {
            IsModOperationInProgress = false;
        }
    }
}
