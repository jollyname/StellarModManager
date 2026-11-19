using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarModManager.Services;
using StellarModManager.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace StellarModManager.ViewModels;

public partial class MainWindowViewModel
{
    private sealed record ProfileExport(string Name, List<string> DeployedModIds);

    private readonly ProfileStorageService profileStorage = new(AppContext.BaseDirectory);
    public ObservableCollection<ModProfile> ModProfiles { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveProfileName))]
    private ModProfile? activeProfile;

    public string ActiveProfileName => ActiveProfile?.Name ?? "Profile";

    [ObservableProperty]
    private bool isProfilesDropdownOpen;

    [ObservableProperty]
    private string newProfileName = "";

    [ObservableProperty]
    private bool isProfileBusy;

    private ModProfile? profilePendingRemoval;

    private void ApplyProfileSnapshot(ProfileSnapshot snapshot)
    {
        var changedProfile = ActiveProfile?.Id != snapshot.ActiveProfileId;
        foreach (var old in ModProfiles.ToList())
        {
            if (snapshot.Profiles.All(profile => profile.Id != old.Id))
                ModProfiles.Remove(old);
        }

        foreach (var profile in snapshot.Profiles)
        {
            var existing = ModProfiles.FirstOrDefault(item => item.Id == profile.Id);
            if (existing == null)
                ModProfiles.Add(profile);
            else
                existing.Name = profile.Name;
        }

        ActiveProfile = ModProfiles.First(profile => profile.Id == snapshot.ActiveProfileId);
        foreach (var profile in ModProfiles)
            profile.IsActive = profile == ActiveProfile;
        OnPropertyChanged(nameof(ActiveProfileName));

        ownedProfileFiles = snapshot.OwnedFiles;
        var externalMods = changedProfile ? Enumerable.Empty<InstalledModInfo>() :
            InstalledMods.Where(mod => mod.ExternalFile is { } file && !ownedProfileFiles.Contains(file.Path)).ToList();

        if (changedProfile)
        {
            CancelRemove();
            WatchExternalMods(snapshot.ActiveProfileId);
        }

        LoadInstalledMods(snapshot.InstalledMods.Concat(externalMods));
        QueueExternalScan();
        if (snapshot.MissingLegacyMods.Count > 0)
            ReportError("StatusMissingLegacyMods", string.Join(", ", snapshot.MissingLegacyMods));
    }

    private async Task RunProfileOperation(string errorKey, Func<Task> operation)
    {
        if (IsProfileBusy || IsInstallingMelonLoader)
        {
            ReportError("StatusProfileBusy");
            return;
        }

        IsProfileBusy = true;
        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            ReportError(errorKey, ex.Message);
        }
        finally
        {
            IsProfileBusy = false;
        }
    }

    [RelayCommand]
    private void ToggleProfilesDropdown() => IsProfilesDropdownOpen = !IsProfilesDropdownOpen;

    [RelayCommand]
    private Task SwitchProfile(ModProfile? profile) => RunProfileOperation("StatusProfileSwitchFailed", async () =>
    {
        if (profile == null || profile.Id == ActiveProfile?.Id)
            return;

        var id = profile.Id;
        var game = GamePath;
        ApplyProfileSnapshot(await Task.Run(() => profileStorage.Switch(id, game)));
    });

    [RelayCommand]
    private Task CreateProfile() => RunProfileOperation("StatusProfileCreateFailed", async () =>
    {
        var name = NewProfileName.Trim();
        if (name.Length == 0)
            return;

        var game = GamePath;
        ApplyProfileSnapshot(await Task.Run(() => profileStorage.Create(name, game)));
        NewProfileName = "";
    });

    [RelayCommand]
    private void DeleteProfile(ModProfile? profile)
    {
        if (profile == null || ModProfiles.Count <= 1 || IsProfileBusy)
            return;

        modPendingRemoval = null;
        profilePendingRemoval = profile;
        ConfirmRemoveTitle = LocalizationService.Instance["ConfirmDeleteProfileTitle"];
        ConfirmRemoveMessage = LocalizationService.Instance.Format("ConfirmDeleteProfileMessage", profile.Name);
        IsConfirmRemoveOpen = true;
    }

    private Task RemoveProfile(ModProfile profile) => RunProfileOperation("StatusProfileDeleteFailed", async () =>
    {
        var game = GamePath;
        ApplyProfileSnapshot(await Task.Run(() => profileStorage.DeleteProfile(profile.Id, game)));
    });

    [RelayCommand]
    private void StartRename(ModProfile? profile)
    {
        if (profile == null) return;
        profile.EditName = profile.Name;
        profile.IsEditing = true;
    }

    [RelayCommand]
    private Task CommitRename(ModProfile? profile) => RunProfileOperation("StatusProfileRenameFailed", async () =>
    {
        if (profile == null)
            return;

        var name = profile.EditName;
        ApplyProfileSnapshot(await Task.Run(() => profileStorage.Rename(profile.Id, name)));
        profile.IsEditing = false;
    });

    [RelayCommand]
    private void CancelRename(ModProfile? profile)
    {
        if (profile != null)
            profile.IsEditing = false;
    }

    private Window? GetMainWindow()
    {
        if (App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }

    [RelayCommand]
    private Task ExportProfile() => RunProfileOperation("StatusProfileExportFailed", async () =>
    {
        if (ActiveProfile == null || GetMainWindow() is not { } window)
            return;

        var profile = new ProfileExport(ActiveProfile.Name, [.. InstalledMods.Where(mod => !mod.IsExternal).Select(mod => mod.Id)]);
        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationService.Instance["ProfileExport"],
            SuggestedFileName = profile.Name,
            DefaultExtension = "json",
            FileTypeChoices = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } }
        });

        if (file == null)
            return;

        await using var stream = await file.OpenWriteAsync();
        if (stream.CanSeek)
            stream.SetLength(0);

        await JsonSerializer.SerializeAsync(stream, profile, new JsonSerializerOptions { WriteIndented = true });
        ReportSuccess("StatusProfileExported", profile.Name);
    });

    [RelayCommand]
    private Task ImportProfile() => RunProfileOperation("StatusProfileImportFailed", async () =>
    {
        if (GetMainWindow() is not { } window)
            return;

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.Instance["ProfileImport"],
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } }
        });

        if (files.Count == 0)
            return;

        await using var stream = await files[0].OpenReadAsync();
        var profile = await JsonSerializer.DeserializeAsync<ProfileExport>(stream);
        if (profile?.Name is not { } name || string.IsNullOrWhiteSpace(name) || profile.DeployedModIds == null
            || profile.DeployedModIds.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException(LocalizationService.Instance["InvalidProfileImport"]);

        name = name.Trim();
        if (ModProfiles.Any(item => item.Name == name))
        {
            ReportError("StatusProfileNameTaken");
            return;
        }

        if (profile.DeployedModIds.Count != 0 && OnlineMods.Count == 0)
            await RefreshMods();

        var catalog = OnlineMods.ToList();
        var mods = new List<OnlineModInfo>();
        var missing = new List<string>();
        foreach (var id in profile.DeployedModIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var mod = catalog.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (mod == null)
                missing.Add(id);
            else
                mods.Add(mod);
        }

        var dependencies = DependencyResolver.Resolve(mods, catalog, _ => false);
        missing.AddRange(dependencies.Missing);
        if (missing.Count > 0)
            throw new InvalidDataException(LocalizationService.Instance.Format("ProfileImportMissingMods",
                string.Join(", ", missing.Distinct(StringComparer.OrdinalIgnoreCase))));

        var snapshot = await WithStagedModsAsync(dependencies.ToInstall, _ => null,
            staged => profileStorage.Import(name, staged));
        ApplyProfileSnapshot(snapshot);
        ReportSuccess("StatusProfileImported", name);
    });
}
