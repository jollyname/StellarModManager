using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace StellarModManager.Models;

public partial class InstalledModInfo : ModInfo
{
    [JsonIgnore]
    public string? ExternalPath { get; init; }

    [JsonIgnore]
    public bool IsExternal => ExternalPath != null;

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
    [NotifyPropertyChangedFor(nameof(IsCurrent))]
    private bool isUpdateAvailable;

    // Shown in the "!" tooltip, empty means no missing dependencies
    [JsonIgnore]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDependencyWarning))]
    private string dependencyWarning = "";

    [JsonIgnore]
    public bool HasDependencyWarning => !string.IsNullOrEmpty(DependencyWarning);

    [JsonIgnore]
    public bool IsCurrent => !IsExternal && !IsUpdateAvailable;

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
