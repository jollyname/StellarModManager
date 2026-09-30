using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media.Imaging;
using StellarModManager.Models;
using StellarModManager.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace StellarModManager.ViewModels;

public partial class MainWindowViewModel
{
    private readonly GameLaunchService gameLaunchService = new();

    /// <summary>Installed mods shown in the card grid.</summary>
    public ObservableCollection<InstalledModInfo> FilteredMods { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryEmptyStateVisible))]
    private bool isLibraryMode;

    /// <summary>Empty-state placeholder, scoped to the Installed tab so it never covers Browse.</summary>
    public bool IsLibraryEmptyStateVisible => IsLibraryMode && IsLibraryEmpty;

    /// <summary>
    /// Card geometry. Both grids bind their card surface to these two values, so an
    /// installed card and a repository card are always the exact same size regardless
    /// of how long the name, author line or description is.
    /// </summary>
    [ObservableProperty]
    private double cardWidth = 320;

    [ObservableProperty]
    private double cardHeight = 340;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailName))]
    private InstalledModInfo? selectedInstalledMod;

    [ObservableProperty]
    private bool isLibraryDetailOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailGalleryEmpty))]
    [NotifyPropertyChangedFor(nameof(IsDetailNoImages))]
    [NotifyPropertyChangedFor(nameof(DetailName))]
    private OnlineModInfo? selectedOnlineMod;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryDetailOnlineMissing))]
    [NotifyPropertyChangedFor(nameof(IsDetailNoImages))]
    [NotifyPropertyChangedFor(nameof(DetailName))]
    private bool isOnlineDetailOpen;

    /// <summary>Repository entry matching the selected installed mod, for its gallery.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryDetailOnlineMissing))]
    [NotifyPropertyChangedFor(nameof(IsDetailNoImages))]
    [NotifyPropertyChangedFor(nameof(IsDetailGalleryEmpty))]
    [NotifyPropertyChangedFor(nameof(IsDetailNoImages))]
    private OnlineModInfo? selectedInstalledModOnline;

    /// <summary>
    /// Drives the single full-page detail overlay, so Installed and Browse can never both
    /// be open and the overlay never needs two competing copies.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLibraryDetailOnlineMissing))]
    [NotifyPropertyChangedFor(nameof(IsDetailNoImages))]
    [NotifyPropertyChangedFor(nameof(IsDetailGalleryEmpty))]
    [NotifyPropertyChangedFor(nameof(IsDetailNoImages))]
    private bool isAnyDetailOpen;

    /// <summary>True when an installed mod is open but has no repository entry to pull images from.</summary>
    public bool IsLibraryDetailOnlineMissing =>
        IsAnyDetailOpen && !IsOnlineDetailOpen && SelectedInstalledModOnline is null;

    /// <summary>True when there is nothing to show in the image pane yet.</summary>
    public bool IsDetailGalleryEmpty
    {
        get
        {
            if (!IsAnyDetailOpen)
                return false;

            ObservableCollection<Bitmap>? gallery = IsOnlineDetailOpen
                ? SelectedOnlineMod?.GalleryImages
                : SelectedInstalledModOnline?.GalleryImages;

            return gallery is null || gallery.Count == 0;
        }
    }

    /// <summary>Name of whichever mod the page is currently showing.</summary>
    public string DetailName => IsOnlineDetailOpen
        ? SelectedOnlineMod?.Name ?? ""
        : SelectedInstalledMod?.Name ?? "";

    /// <summary>
    /// Label/value lines for the details panel. There is no richer "more info" blob in
    /// mods.json, so this is assembled from the fields that genuinely exist and the panel
    /// falls back to a "no details" state when a mod carries none of them.
    /// </summary>
    public ObservableCollection<DetailInfoRow> DetailInfoRows { get; } = new();

    public bool IsDetailInfoEmpty => DetailInfoRows.Count == 0;

    /// <summary>Version timeline, newest first, with the trailing dot flagged.</summary>
    public ObservableCollection<VersionTimelineEntry> DetailVersions { get; } = new();

    /// <summary>
    /// Flattened image list for the horizontal strip. The three cases (online, matched
    /// installed, icon-only fallback) used to be three separate ItemsControls; mirroring
    /// them into one collection keeps a single strip and a single empty state.
    /// </summary>
    public ObservableCollection<Bitmap> DetailGalleryImages { get; } = new();

    [ObservableProperty]
    private bool isLightboxOpen;

    [ObservableProperty] private Bitmap? lightboxImage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LightboxCounter))]
    private IReadOnlyList<Bitmap> lightboxImages = Array.Empty<Bitmap>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMultipleLightboxImages))]
    [NotifyPropertyChangedFor(nameof(LightboxCounter))]
    [NotifyCanExecuteChangedFor(nameof(StepLightboxCommand))]
    private int lightboxIndex;


    /// <summary>
    /// True when there are no screenshots to show and no icon to fall back to either.
    /// </summary>
    public bool IsDetailNoImages => IsDetailGalleryEmpty && !IsLibraryDetailOnlineMissing;

    /// <summary>True when neither an update note nor a changelog entry exists.</summary>
    public bool IsDetailVersionsEmpty => IsOnlineDetailOpen
        ? (SelectedOnlineMod?.Changelog.Count ?? 0) == 0
        : string.IsNullOrWhiteSpace(SelectedInstalledMod?.UpdateNotes);

    [RelayCommand]
    private void CloseDetail()

    {
        if (IsLibraryDetailOpen)
            CloseLibraryDetail();
        else
            CloseOnlineDetail();
    }

    [RelayCommand]
    private void OpenLightbox(Bitmap? image)
    {
        if (image is null)
            return;

        int index = DetailGalleryImages.IndexOf(image);

        LightboxImages = DetailGalleryImages.ToList();
        LightboxIndex = index >= 0 ? index : 0;
        LightboxImage = LightboxImages[LightboxIndex];
        IsLightboxOpen = true;
    }

    [RelayCommand]
    private void StepLightbox(int delta)
    {
        if (LightboxImages.Count == 0)
            return;

        LightboxIndex = (LightboxIndex + delta + LightboxImages.Count) % LightboxImages.Count;
        LightboxImage = LightboxImages[LightboxIndex];
    }

    [RelayCommand]
    private void CloseLightbox()
    {
        IsLightboxOpen = false;
        LightboxImage = null;
        LightboxImages = Array.Empty<Bitmap>();
        LightboxIndex = 0;
    }

    public bool HasMultipleLightboxImages => LightboxImages.Count > 1;

    public string LightboxCounter =>
        $"{LightboxIndex + 1} / {LightboxImages.Count}";

    /// <summary>
    /// Rebuilds the three side-by-side panels from whichever mod is open. Called on every
    /// selection change and again whenever the gallery or changelog streams in, so the
    /// rows never lag behind the data.
    /// </summary>
    private void RefreshDetailPanels()
    {
        if (!IsAnyDetailOpen)
        {
            DetailInfoRows.Clear();
            DetailVersions.Clear();
            DetailGalleryImages.Clear();
            return;
        }

        OnlineModInfo? online = IsOnlineDetailOpen ? SelectedOnlineMod : SelectedInstalledModOnline;
        InstalledModInfo? installed = IsOnlineDetailOpen ? null : SelectedInstalledMod;
        LocalizationService loc = LocalizationService.Instance;

        DetailInfoRows.Clear();

        if (online is not null)
        {
            AddInfoRow(loc["AuthorLabel"], online.Author);
            AddInfoRow(loc["VersionLabel"], online.Version);
            AddInfoRow(loc["CategoryLabel"], online.CategoryName);
            AddInfoRow(loc["SourceLabel"], $"{online.RepoOwner}/{online.RepoName}");
        }

        if (installed is not null)
        {
            AddInfoRow(loc["AuthorLabel"], installed.Author);
            AddInfoRow(loc["VersionLabel"], installed.Version);
            AddInfoRow(loc["CategoryLabel"], installed.CategoryName);
            AddInfoRow(loc["FolderLabel"], installed.LibraryFolder);

            if (installed.IsUpdateAvailable)
                AddInfoRow(loc["Update"], installed.LatestVersion);
        }

        OnPropertyChanged(nameof(IsDetailInfoEmpty));

        // version timeline
        DetailVersions.Clear();

        if (online is not null)
        {
            for (int i = 0; i < online.Changelog.Count; i++)
            {
                ChangelogEntry entry = online.Changelog[i];
                DetailVersions.Add(new VersionTimelineEntry(entry.Version.ToString(), entry.Notes, i == online.Changelog.Count - 1));
            }
        }
        else if (installed is not null && !string.IsNullOrWhiteSpace(installed.UpdateNotes))
        {
            DetailVersions.Add(new VersionTimelineEntry(installed.Version, installed.UpdateNotes!, isLast: true));
        }

        // horizontal image strip
        DetailGalleryImages.Clear();

        if (online is not null)
        {
            foreach (Bitmap image in online.GalleryImages)
                DetailGalleryImages.Add(image);
        }
        else if (installed?.IconImage is not null)
        {
            DetailGalleryImages.Add(installed.IconImage);
        }
    }

    private void AddInfoRow(string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        string trimmed = value.Trim();

        if (trimmed.Length == 0)
            return;

        DetailInfoRows.Add(new DetailInfoRow(label, trimmed));
    }

    /// <summary>
    /// Gallery images and changelog entries stream in one at a time after the page opens, so
    /// these computed flags have to be re-raised whenever those collections change or the
    /// "nothing here" placeholders stick around over real content.
    /// </summary>
    partial void OnSelectedOnlineModChanging(OnlineModInfo? oldValue, OnlineModInfo? newValue)
    {
        DetachGallery(oldValue);
        AttachGallery(newValue);
    }

    partial void OnSelectedInstalledModOnlineChanging(OnlineModInfo? oldValue, OnlineModInfo? newValue)
    {
        DetachGallery(oldValue);
        AttachGallery(newValue);
    }

    private void AttachGallery(OnlineModInfo? mod)
    {
        if (mod is null)
            return;

        mod.GalleryImages.CollectionChanged += OnGalleryCollectionChanged;
        mod.Changelog.CollectionChanged += OnGalleryCollectionChanged;
    }

    private void DetachGallery(OnlineModInfo? mod)
    {
        if (mod is null)
            return;

        mod.GalleryImages.CollectionChanged -= OnGalleryCollectionChanged;
        mod.Changelog.CollectionChanged -= OnGalleryCollectionChanged;
    }

    private void OnGalleryCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsDetailGalleryEmpty));
        OnPropertyChanged(nameof(IsDetailNoImages));
        OnPropertyChanged(nameof(IsDetailVersionsEmpty));
        RefreshDetailPanels();
    }

    [ObservableProperty]
    private bool isLaunching;

    [ObservableProperty]
    private bool isGameRunning;

    [ObservableProperty]
    private string launchStatus = "";

    [ObservableProperty]
    private string? selectedModSourceUrl;

    [ObservableProperty]
    private bool hasLibraryMods;

    [ObservableProperty]
    private bool hasInstalledMods;

    public string InstalledCountText =>
        LocalizationService.Instance.Format("ModsCount", InstalledMods.Count);

    public string OnlineModsCountText =>
        LocalizationService.Instance.Format("ModsAvailable", OnlineMods.Count);

    public string PlayButtonText
    {
        get
        {
            if (IsGameRunning) return LocalizationService.Instance["Running"];
            if (IsLaunching) return LocalizationService.Instance["Launching"];
            return LocalizationService.Instance["Play"];
        }
    }

    partial void OnIsLaunchingChanged(bool value) => OnPropertyChanged(nameof(PlayButtonText));

    partial void OnIsGameRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(PlayButtonText));
        LaunchStatus = value ? LocalizationService.Instance["GameRunning"] : "";
    }

    // Mirrors the library into the grid collection.
    public void SyncLibrary()
    {
        FilteredMods.Clear();

        foreach (var mod in InstalledMods)
            FilteredMods.Add(mod);

        bool any = FilteredMods.Count > 0;

        if (HasLibraryMods != any)
            HasLibraryMods = any;

        bool installed = InstalledMods.Count > 0;

        if (HasInstalledMods != installed)
            HasInstalledMods = installed;

        OnPropertyChanged(nameof(InstalledCountText));

        if (!any)
            CloseLibraryDetail();
    }

    public void EnsureCardIcon(InstalledModInfo mod) => ModIconLoader.TryLoad(mod);

    [RelayCommand]
    private void ShowLibrary()
    {
        IsLibraryMode = true;
        CloseLibraryDetail();
    }

    [RelayCommand]
    private void ShowBrowse()
    {
        IsLibraryMode = false;
        CloseLibraryDetail();

        if (OnlineMods.Count == 0)
            _ = RefreshMods();
    }

    [RelayCommand]
    private void SelectInstalledMod(InstalledModInfo mod)
    {
        if (SelectedInstalledMod is not null && !ReferenceEquals(SelectedInstalledMod, mod))
            SelectedInstalledMod.IsSelected = false;

        mod.IsSelected = true;
        SelectedInstalledMod = mod;
        IsLibraryDetailOpen = true;
        IsAnyDetailOpen = true;

        var online = OnlineMods.FirstOrDefault(o => o.Id == mod.Id);
        SelectedInstalledModOnline = online;

        if (online is not null)
        {
            _ = repositoryService.LoadGalleryAsync(online);
            _ = repositoryService.LoadChangelogAsync(online);
        }

        SelectedModSourceUrl = online is null || string.IsNullOrWhiteSpace(online.RepoName)
            ? null
            : $"https://github.com/{online.RepoOwner}/{online.RepoName}";

        // last, so the panels see the resolved repository entry and anything already cached
        RefreshDetailPanels();
    }

    [RelayCommand]
    private void CloseLibraryDetail()
    {
        IsLibraryDetailOpen = false;
        IsAnyDetailOpen = false;
        SelectedInstalledModOnline = null;

        if (SelectedInstalledMod is not null)
        {
            SelectedInstalledMod.IsSelected = false;
            SelectedInstalledMod = null;
        }

        SelectedModSourceUrl = null;
    }

    /// <summary>
    /// Opens the browse detail view for a repository mod. The two panels are mutually
    /// exclusive, so picking a card always leaves exactly one open.
    /// </summary>
    [RelayCommand]
    private void SelectOnlineMod(OnlineModInfo? mod)
    {
        if (mod is null)
            return;

        CloseLibraryDetail();

        if (SelectedOnlineMod is not null && !ReferenceEquals(SelectedOnlineMod, mod))
            SelectedOnlineMod.IsSelected = false;

        mod.IsSelected = true;
        SelectedOnlineMod = mod;
        IsOnlineDetailOpen = true;
        IsAnyDetailOpen = true;

        // The full page leads with screenshots, so pull them as soon as it opens.
        _ = repositoryService.LoadGalleryAsync(mod);
        _ = repositoryService.LoadChangelogAsync(mod);

        RefreshDetailPanels();
    }

    [RelayCommand]
    private void CloseOnlineDetail()
    {
        IsOnlineDetailOpen = false;
        IsAnyDetailOpen = false;

        if (SelectedOnlineMod is not null)
        {
            SelectedOnlineMod.IsSelected = false;
            SelectedOnlineMod = null;
        }
    }

    [RelayCommand]
    private void OpenModFolder(InstalledModInfo mod)
    {
        string path = string.IsNullOrWhiteSpace(mod.LibraryFolder)
            ? Path.Combine(AppContext.BaseDirectory, "Library", mod.Id)
            : mod.LibraryFolder;

        ShellService.OpenFolder(path);
    }

    [RelayCommand]
    private void OpenSourceRepository()
    {
        if (!string.IsNullOrWhiteSpace(SelectedModSourceUrl))
            ShellService.OpenUrl(SelectedModSourceUrl);
    }

    [RelayCommand]
    private async Task ToggleMod(InstalledModInfo mod)
    {
        if (mod.IsToggling)
            return;

        if (GamePath == "No game selected" || !Directory.Exists(GamePath))
        {
            LaunchStatus = LocalizationService.Instance["LaunchBlockedNoGame"];
            return;
        }

        bool target = !mod.IsEnabled;

        mod.IsEnabled = target; // optimistic so the switch reacts on the same frame
        mod.IsToggling = true;

        try
        {
            string libraryPath = Path.Combine(AppContext.BaseDirectory, "Library", mod.Id);

            if (target)
                await Task.Run(() => deploymentService.DeployMod(libraryPath, GamePath));
            else
                await Task.Run(() => deploymentService.RemoveDeployedFiles(libraryPath, GamePath));
        }
        catch (Exception ex)
        {
            mod.IsEnabled = !target;
            LaunchStatus = LocalizationService.Instance.Format("ToggleFailed", mod.Name, ex.Message);
        }
        finally
        {
            mod.IsToggling = false;
        }
    }

    [RelayCommand]
    private async Task Play()
    {
        if (IsLaunching || IsGameRunning)
            return;

        LaunchStatus = "";

        if (GamePath == "No game selected" || !Directory.Exists(GamePath))
        {
            LaunchStatus = LocalizationService.Instance["LaunchBlockedNoGame"];
            return;
        }

        if (!IsMelonLoaderValid)
        {
            LaunchStatus = LocalizationService.Instance["LaunchBlockedNoLoader"];
            return;
        }

        IsLaunching = true;
        LaunchStatus = LocalizationService.Instance["Validating"];

        try
        {
            // deploy anything the user enabled but never copied into the game
            foreach (var mod in InstalledMods.Where(m => m.IsEnabled))
            {
                string libraryPath = Path.Combine(AppContext.BaseDirectory, "Library", mod.Id);

                if (!deploymentService.IsDeployed(libraryPath))
                    await Task.Run(() => deploymentService.DeployMod(libraryPath, GamePath));
            }

            Process? process = await Task.Run(() => gameLaunchService.Launch(GamePath));

            if (process is null)
            {
                LaunchStatus = LocalizationService.Instance["LaunchNotFound"];
                return;
            }

            lastLaunchedProcess = process;
            IsGameRunning = true;

            // keep the pill honest for a beat so the transition is readable
            await Task.Delay(450);
        }
        catch (Exception ex)
        {
            LaunchStatus = LocalizationService.Instance.Format("LaunchFailed", ex.Message);
        }
        finally
        {
            IsLaunching = false;
        }

        if (IsGameRunning && lastLaunchedProcess is not null)
            _ = WatchGameAsync(lastLaunchedProcess);
    }

    private Process? lastLaunchedProcess;

    private async Task WatchGameAsync(Process process)
    {
        try
        {
            while (!process.HasExited)
                await Task.Delay(600);
        }
        catch
        {
            // process handle died with the game
        }

        if (ReferenceEquals(lastLaunchedProcess, process))
            lastLaunchedProcess = null;

        IsGameRunning = false;
    }
}
