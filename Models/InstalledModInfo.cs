using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace StellarModManager.Models;

public partial class InstalledModInfo : ModInfo
{
    //Base properties in ModInfo class

    // UI only
    [JsonIgnore]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsDeploy), nameof(IsDeployedAndCurrent))]
    private bool isDeployed;

    [JsonIgnore]
    [ObservableProperty]
    private bool isDeploying;

    [JsonIgnore]
    [ObservableProperty]
    private double deployProgress;

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
    [NotifyPropertyChangedFor(nameof(NeedsDeploy), nameof(IsDeployedAndCurrent))]
    private bool isUpdateAvailable;

    // Shown in the "!" tooltip, empty means no missing dependencies
    [JsonIgnore]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDependencyWarning))]
    private string dependencyWarning = "";

    [JsonIgnore]
    public bool HasDependencyWarning => !string.IsNullOrEmpty(DependencyWarning);

    // Deploying to the game is the next step once the mod is up to date
    [JsonIgnore]
    public bool NeedsDeploy => !IsDeployed && !IsUpdateAvailable;

    [JsonIgnore]
    public bool IsDeployedAndCurrent => IsDeployed && !IsUpdateAvailable;

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