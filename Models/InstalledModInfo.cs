using CommunityToolkit.Mvvm.ComponentModel;
using StellarModManager.Services;
using System.Text.Json.Serialization;

namespace StellarModManager.Models;

public partial class InstalledModInfo : ModInfo
{
    [JsonIgnore]
    internal ExternalDll? ExternalFile { get; init; }

    [JsonIgnore]
    public bool IsExternal => ExternalFile != null;

    [JsonIgnore]
    [ObservableProperty]
    private bool isRemoving;

    [JsonIgnore]
    [ObservableProperty]
    private bool isUpdating;

    [JsonIgnore]
    [ObservableProperty]
    private double updateProgress;

    [JsonIgnore]
    [ObservableProperty]
    private bool isUpdateAvailable;

    // Shown in the "!" tooltip, empty means no missing dependencies
    [JsonIgnore]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDependencyWarning))]
    private string dependencyWarning = "";

    [JsonIgnore]
    public bool HasDependencyWarning => !string.IsNullOrEmpty(DependencyWarning);


    [JsonIgnore]
    [ObservableProperty]
    private string? latestVersion;

    [JsonIgnore]
    [ObservableProperty]
    private string? updateNotes;

    [JsonIgnore]
    [ObservableProperty]
    private OnlineModInfo? online;
}