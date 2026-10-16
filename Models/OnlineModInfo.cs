using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using StellarModManager.Services;
using System;
using System.Collections.Generic; // Lists
using System.Collections.ObjectModel; // image collection
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace StellarModManager.Models;

// Reuse for regular tags and ai tag
public record ModTag(string Text, bool IsAssisted = false, bool IsVibecoded = false);

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
    public ObservableCollection<ModVideo> VideoCards { get; } = new();

    public void RebuildVideos()
    {
        VideoCards.Clear();

        foreach (ModVideo card in VideoLinks.Cards(Videos))
            VideoCards.Add(card);
    }

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

    // I just put AI tags in here so they can't be used as regular tags
    private static readonly HashSet<string> ReservedTags = new(StringComparer.OrdinalIgnoreCase)
    { "ai-free", "ai-assisted", "vibecoded" };

    [JsonIgnore]
    public List<ModTag> _Tags { get; private set; } = new();

    public void RebuildTags()
    {
        var list = new List<ModTag>();

        // AI badge
        switch ((Ai ?? "").Trim().ToLowerInvariant())
        {
            case "ai-free": list.Add(new ModTag("AI-free", IsAssisted: false, IsVibecoded: false)); break;
            case "ai-assisted": list.Add(new ModTag("AI-assisted", IsAssisted: true)); break;
            case "vibecoded": list.Add(new ModTag("Vibecoded", IsVibecoded: true)); break;
        }

        list.AddRange((Tags ?? new())
            .Select(tag => (tag ?? "").Trim())
            .Where(tag => tag.Length is > 0 and <= 24 && !ReservedTags.Contains(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .Select(tag => new ModTag(tag)));

        _Tags = list;
        OnPropertyChanged(nameof(_Tags));
    }

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