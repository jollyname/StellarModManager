using CommunityToolkit.Mvvm.ComponentModel;
using StellarModManager.Services;
using System.Text.Json.Serialization;

namespace StellarModManager.Models;

public partial class OnlineModInfo : ModInfo
{
    public OnlineModInfo()
    {
        LocalizationService.Instance.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(InstallButtonText));
        };
    }

    //Base properties in ModInfo class

    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = "";

    // UI only
    public string RepoName { get; set; } = "";

    [JsonIgnore]
    [ObservableProperty]
    private bool isInstalling;

    [JsonIgnore]
    [ObservableProperty]
    private double downloadProgress;

    [JsonIgnore]
    [ObservableProperty]
    private bool isInstalled;

    [JsonIgnore]
    public string InstallButtonText => IsInstalled ? LocalizationService.Instance["Reinstall"] : LocalizationService.Instance["Install"];

    partial void OnIsInstalledChanged(bool value)
    {
        OnPropertyChanged(nameof(InstallButtonText));
    }
}