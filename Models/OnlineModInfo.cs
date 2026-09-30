using CommunityToolkit.Mvvm.ComponentModel;
using StellarModManager.Services;
using System;
using System.Text.Json.Serialization;
using System.Collections.Generic; // Lists
using Avalonia.Media;
using Avalonia.Media.Immutable;
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
            OnPropertyChanged(nameof(StatsText));
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
    [NotifyPropertyChangedFor(nameof(StatsText))]
    private long downloads;

    [JsonIgnore]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatsText))]
    private DateTime? lastUpdated;

    [JsonIgnore]
    public string StatsText => LastUpdated is DateTime updated
        ? string.Format(LocalizationService.Instance["StatsFormat"], Downloads, Ago(updated))
        : "";

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
    private bool isInstalling;

    [JsonIgnore]
    [ObservableProperty]
    private double downloadProgress;

    [JsonIgnore]
    [ObservableProperty]
    private bool isInstalled;

    [JsonIgnore]
    [ObservableProperty]
    private bool isSelected;

    [JsonIgnore]
    public string InstallButtonText => IsInstalled ? LocalizationService.Instance["Reinstall"] : LocalizationService.Instance["Install"];

    // Card presentation, shared with InstalledModInfo so both grids look the same.

    [JsonIgnore]
    public string Initial => ModPresentation.Initial(Name);

    [JsonIgnore]
    public IBrush AvatarBackground { get; private set; } = ModPresentation.AvatarBrush("");

    [JsonIgnore]
    public string CategoryName { get; private set; } = "Utility";

    [JsonIgnore]
    public IBrush CategoryBackground { get; private set; } = new ImmutableSolidColorBrush(Color.Parse("#ffa800"));

    [JsonIgnore]
    public IBrush ChipTextBrush => ModPresentation.ChipText;

    [JsonIgnore]
    public string ByAuthorVersion => LocalizationService.Instance.Format("ByAuthorVersion", Author, Version);

    [JsonIgnore]
    public string CardDescription
    {
        get
        {
            string description = Description?.Trim() ?? "";

            return description.Length == 0
                ? LocalizationService.Instance["NoDescription"]
                : description;
        }
    }

    public OnlineModInfo ResolvePresentation()
    {
        (string category, string color) = ModPresentation.Category(Name, Description);

        CategoryName = category;
        CategoryBackground = new ImmutableSolidColorBrush(Color.Parse(color));
        AvatarBackground = ModPresentation.AvatarBrush(Name ?? Id);

        return this;
    }

    partial void OnIsInstalledChanged(bool value)
    {
        OnPropertyChanged(nameof(InstallButtonText));
    }
}
