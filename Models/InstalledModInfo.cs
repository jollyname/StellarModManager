using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using StellarModManager.Services;
using System;
using System.Text.Json.Serialization;

namespace StellarModManager.Models;

public partial class InstalledModInfo : ModInfo
{
    public InstalledModInfo()
    {
        LocalizationService.Instance.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ByAuthorVersion));
            OnPropertyChanged(nameof(CardDescription));
        };
    }

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

    [JsonIgnore]
    [ObservableProperty]
    private string? updateNotes;

    // Library card state

    [JsonIgnore]
    [ObservableProperty]
    private bool isEnabled = true;

    [JsonIgnore]
    [ObservableProperty]
    private bool isToggling;

    [JsonIgnore]
    [ObservableProperty]
    private bool isSelected;

    [JsonIgnore]
    public string LibraryFolder { get; set; } = "";

    [JsonIgnore]
    public Bitmap? IconImage { get; set; }

    [JsonIgnore]
    public bool IconLoadAttempted { get; set; }

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

    [JsonIgnore]
    [ObservableProperty]
    private OnlineModInfo? online;

    [JsonIgnore]
    public string? LatestVersionLine
    {
        get
        {
            if (!IsUpdateAvailable || string.IsNullOrWhiteSpace(LatestVersion))
                return null;

            return LocalizationService.Instance.Format("UpdateToVersion", LatestVersion);
        }
    }

    public InstalledModInfo ResolvePresentation()
    {
        (string category, string color) = ModPresentation.Category(Name, Description);

        CategoryName = category;
        CategoryBackground = new ImmutableSolidColorBrush(Color.Parse(color));
        AvatarBackground = ModPresentation.AvatarBrush(Name ?? Id);

        return this;
    }

    partial void OnIsUpdateAvailableChanged(bool value) => OnPropertyChanged(nameof(LatestVersionLine));

    partial void OnLatestVersionChanged(string? value) => OnPropertyChanged(nameof(LatestVersionLine));
}
