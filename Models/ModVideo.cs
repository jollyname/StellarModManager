using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StellarModManager.Models;

public partial class ModVideo : ObservableObject
{
    public ModVideo(string url, string? youTubeId, string? posterUrl, string label)
    {
        Url = url;
        YouTubeId = youTubeId;
        PosterUrl = posterUrl;
        Label = label;
    }

    public string Url { get; }

    public string? YouTubeId { get; }

    public string? PosterUrl { get; }

    public string Label { get; }

    [ObservableProperty]
    private Bitmap? poster;
}
