using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StellarModManager.Services;
using StellarModManager.Models;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace StellarModManager.ViewModels;

public partial class MainWindowViewModel
{
    public ObservableCollection<ModProfile> ModProfiles { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveProfileName))]
    private ModProfile? activeProfile;

    public string ActiveProfileName => ActiveProfile?.Name ?? "Profile";

    [ObservableProperty]
    private bool isProfilesDropdownOpen;

    [ObservableProperty]
    private string newProfileName = "";

    private void LoadProfiles()
    {
        var (profiles, activeName) = settingsService.LoadModProfiles();
        ModProfiles.Clear();

        bool noProfiles = profiles.Count == 0;

        if (noProfiles)
        {
            profiles.Add(new ModProfile
            {
                Name = "Default",
                DeployedModIds = InstalledMods.Where(m => m.IsDeployed).Select(m => m.Id).ToList()
            });
        }

        foreach (var p in profiles)
            ModProfiles.Add(p);

        ActiveProfile = ModProfiles.FirstOrDefault(p => p.Name == activeName) ?? ModProfiles.First();
        SyncActiveFlag();

        if (noProfiles)
            SaveProfiles();
    }

    private void SyncActiveFlag()
    {
        foreach (var p in ModProfiles)
            p.IsActive = p == ActiveProfile;
    }

    private void SaveProfiles()
    {
        settingsService.SaveModProfiles(ModProfiles.ToList(), ActiveProfile?.Name ?? "");
    }

    private RelayCommand? _toggleProfilesDropdownCommand;
    public RelayCommand ToggleProfilesDropdownCommand => _toggleProfilesDropdownCommand ??= new RelayCommand(ToggleProfilesDropdown);
    private void ToggleProfilesDropdown()
    {
        IsProfilesDropdownOpen = !IsProfilesDropdownOpen;
    }

    private AsyncRelayCommand<ModProfile>? _switchProfileCommand;
    public AsyncRelayCommand<ModProfile> SwitchProfileCommand => _switchProfileCommand ??= new AsyncRelayCommand<ModProfile>(SwitchProfile);
    private async Task SwitchProfile(ModProfile? profile)
    {
        if (profile == null || profile == ActiveProfile) return;

        if (ActiveProfile != null)
        {
            ActiveProfile.DeployedModIds = InstalledMods
                .Where(m => m.IsDeployed)
                .Select(m => m.Id)
                .ToList();
        }

        ActiveProfile = profile;
        SyncActiveFlag();
        SaveProfiles();

        if (GamePath == "No game selected") return;

        foreach (var mod in InstalledMods.ToList())
        {
            bool shouldDeploy = profile.DeployedModIds.Contains(mod.Id);

            if (mod.IsDeployed && !shouldDeploy)
            {
                string libPath = Path.Combine(AppContext.BaseDirectory, "Library", mod.Id);
                await Task.Run(() => deploymentService.RemoveDeployedFiles(libPath, GamePath));
                mod.IsDeployed = false;
            }
            else if (!mod.IsDeployed && shouldDeploy)
            {
                await DeployOneAsync(mod);
            }
        }

        RefreshDependencyStatuses();
    }

    private RelayCommand? _createProfileCommand;
    public RelayCommand CreateProfileCommand => _createProfileCommand ??= new RelayCommand(CreateProfile);
    private void CreateProfile()
    {
        string trimmed = NewProfileName.Trim();
        if (string.IsNullOrEmpty(trimmed)) return;
        if (ModProfiles.Any(p => p.Name == trimmed)) return;

        var profile = new ModProfile
        {
            Name = trimmed,
            DeployedModIds = InstalledMods.Where(m => m.IsDeployed).Select(m => m.Id).ToList()
        };

        ModProfiles.Add(profile);
        NewProfileName = "";
        SaveProfiles();
        _ = SwitchProfile(profile);
    }

    private RelayCommand<ModProfile>? _deleteProfileCommand;
    public RelayCommand<ModProfile> DeleteProfileCommand => _deleteProfileCommand ??= new RelayCommand<ModProfile>(DeleteProfile);
    private void DeleteProfile(ModProfile? profile)
    {
        if (profile == null || ModProfiles.Count <= 1) return;

        bool wasActive = profile == ActiveProfile;
        ModProfiles.Remove(profile);

        if (wasActive)
            _ = SwitchProfile(ModProfiles.First());
        else
            SaveProfiles();
    }

    private RelayCommand<ModProfile>? _startRenameCommand;
    public RelayCommand<ModProfile> StartRenameCommand => _startRenameCommand ??= new RelayCommand<ModProfile>(StartRename);
    private void StartRename(ModProfile? profile)
    {
        if (profile == null) return;
        profile.EditName = profile.Name;
        profile.IsEditing = true;
    }

    private RelayCommand<ModProfile>? _commitRenameCommand;
    public RelayCommand<ModProfile> CommitRenameCommand => _commitRenameCommand ??= new RelayCommand<ModProfile>(CommitRename);
    private void CommitRename(ModProfile? profile)
    {
        if (profile == null) return;
        string trimmed = profile.EditName.Trim();

        if (string.IsNullOrEmpty(trimmed) || ModProfiles.Any(p => p != profile && p.Name == trimmed))
        {
            profile.IsEditing = false;
            return;
        }

        profile.Name = trimmed;
        profile.IsEditing = false;

        if (profile == ActiveProfile)
            OnPropertyChanged(nameof(ActiveProfileName));

        SaveProfiles();
    }

    private RelayCommand<ModProfile>? _cancelRenameCommand;
    public RelayCommand<ModProfile> CancelRenameCommand => _cancelRenameCommand ??= new RelayCommand<ModProfile>(CancelRename);
    private void CancelRename(ModProfile? profile)
    {
        if (profile == null) return;
        profile.IsEditing = false;
    }

    private Window? GetMainWindow()
    {
        if (App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }

    private AsyncRelayCommand? _exportProfileCommand;
    public AsyncRelayCommand ExportProfileCommand => _exportProfileCommand ??= new AsyncRelayCommand(ExportProfile);
    private async Task ExportProfile()
    {
        if (ActiveProfile == null) return;

        var window = GetMainWindow();
        if (window == null) return;

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationService.Instance["ProfileExport"],
            SuggestedFileName = ActiveProfile.Name,
            DefaultExtension = "json",
            FileTypeChoices = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } }
        });

        if (file == null) return;

        try
        {
            var json = JsonSerializer.Serialize(ActiveProfile, new JsonSerializerOptions { WriteIndented = true });
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(json);
            ReportSuccess("StatusProfileExported", ActiveProfile.Name);
        }
        catch (Exception ex)
        {
            ReportError("StatusProfileExportFailed", ex.Message);
        }
    }

    private AsyncRelayCommand? _importProfileCommand;
    public AsyncRelayCommand ImportProfileCommand => _importProfileCommand ??= new AsyncRelayCommand(ImportProfile);
    private async Task ImportProfile()
    {
        var window = GetMainWindow();
        if (window == null) return;

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.Instance["ProfileImport"],
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } }
        });

        if (files.Count == 0) return;

        try
        {
            await using var stream = await files[0].OpenReadAsync();
            var profile = await JsonSerializer.DeserializeAsync<ModProfile>(stream);

            if (profile == null) throw new Exception("Invalid profile file.");

            if (ModProfiles.Any(p => p.Name == profile.Name))
            {
                ReportError("StatusProfileNameTaken");
                return;
            }

            profile.IsActive = false;
            profile.IsEditing = false;
            ModProfiles.Add(profile);
            SaveProfiles();
            ReportSuccess("StatusProfileImported", profile.Name);
        }
        catch (Exception ex)
        {
            ReportError("StatusProfileImportFailed", ex.Message);
        }
    }

    internal void NotifyProfileDeployed(string modId)
    {
        if (ActiveProfile != null && !ActiveProfile.DeployedModIds.Contains(modId))
        {
            ActiveProfile.DeployedModIds.Add(modId);
            SaveProfiles();
        }
    }

    internal void NotifyProfileUndeployed(string modId)
    {
        if (ActiveProfile != null)
        {
            ActiveProfile.DeployedModIds.Remove(modId);
            SaveProfiles();
        }
    }

    internal void RemoveModFromProfiles(string modId)
    {
        foreach (var p in ModProfiles)
            p.DeployedModIds.Remove(modId);

        SaveProfiles();
    }
}
