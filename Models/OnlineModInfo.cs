using CommunityToolkit.Mvvm.ComponentModel;
using StellarModManager.Services;
using System;
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
            OnPropertyChanged(nameof(UpdatedText));
            OnPropertyChanged(nameof(DownloadsText));
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
    public string? RepoUrl => string.IsNullOrEmpty(RepoOwner) || string.IsNullOrEmpty(RepoName)
        ? null
        : $"https://github.com/{RepoOwner}/{RepoName}";

    [JsonIgnore]
    public ObservableCollection<ChangelogEntry> Changelog { get; } = new();

    [JsonIgnore]
    public Task? ChangelogTask { get; set; }

    [JsonIgnore]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadsText))]
    private long downloads;

    [JsonIgnore]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdatedText))]
    private DateTime? lastUpdated;

    [JsonIgnore]
    public string DownloadsText => string.Format(LocalizationService.Instance["DownloadsFormat"], Downloads);

    [JsonIgnore]
    public string UpdatedText => LastUpdated is DateTime updated ? Ago(updated) : "";

    private static string Ago(DateTime updated)
    {
        var loc = LocalizationService.Instance;
        int days = (int)(DateTime.UtcNow - updated.ToUniversalTime()).TotalDays;
        return days switch
        {
            < 1 => loc["AgoToday"],
            1 => loc["AgoYesterday"],
            < 30 => string.Format(loc["AgoDays"], days),
            _ => updated.ToLocalTime().ToString("d", System.Globalization.CultureInfo.CurrentUICulture)
        };
    }

    [JsonIgnore]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInstalledState), nameof(ShowUpdateState), nameof(ShowNoState))]
    private bool isInstalling;

    [JsonIgnore]
    [ObservableProperty]
    private double downloadProgress;

    [JsonIgnore]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInstalledState), nameof(ShowUpdateState), nameof(ShowNoState))]
    private bool isInstalled;

    // Set when the installed copy is older than this version
    [JsonIgnore]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInstalledState), nameof(ShowUpdateState))]
    private bool hasUpdate;

    // State column: exactly one of these (or IsInstalling) is shown
    [JsonIgnore]
    public bool ShowInstalledState => IsInstalled && !HasUpdate && !IsInstalling;

    [JsonIgnore]
    public bool ShowUpdateState => HasUpdate && !IsInstalling;

    [JsonIgnore]
    public bool ShowNoState => !IsInstalled && !IsInstalling;

    [JsonIgnore]
    public string InstallButtonText => IsInstalled ? LocalizationService.Instance["Reinstall"] : LocalizationService.Instance["Install"];

    partial void OnIsInstalledChanged(bool value)
    {
        OnPropertyChanged(nameof(InstallButtonText));
    }
}