using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace StellarModManager.Models;

public partial class InstalledModInfo : ModInfo
{
    //Base properties in ModInfo class

    // UI only
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
    private bool isUpdateAvailable;

    [JsonIgnore]
    [ObservableProperty]
    private string? latestVersion;
}