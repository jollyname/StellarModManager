using CommunityToolkit.Mvvm.ComponentModel;
using StellarModManager.Services;
using System.Text.Json.Serialization;
using System.Collections.Generic; // Lists
using Avalonia.Media.Imaging;
using System.Collections.ObjectModel; // image collection
using System.Threading.Tasks;

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

    [JsonIgnore] // lazy fix hehe
    public string ThumbnailUrl { get; set; } = "";

    [JsonIgnore]
    [ObservableProperty]
    private Bitmap? thumbnailImage;

    [JsonIgnore]
    public List<string> ImageUrls { get; set; } = new();

    [JsonIgnore]
    public ObservableCollection<Bitmap> GalleryImages { get; } = new(); 

    [JsonIgnore]
    public bool GalleryLoaded { get; set; }

    // UI only
    public string RepoName { get; set; } = "";

    public string RepoOwner { get; set; } = "";

    [JsonIgnore]
    public ObservableCollection<ChangelogEntry> Changelog { get; } = new();

    [JsonIgnore]
    public Task? ChangelogTask { get; set; }

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