using StellarModManager.Models;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace StellarModManager.Services;

// mod.json "videos" holds absolute page URLs. YouTube ones get a poster image.
public static class VideoLinks
{
    private static readonly Regex YouTubeIdPattern = new(@"^[A-Za-z0-9_-]{11}$", RegexOptions.CultureInvariant);

    public static IEnumerable<ModVideo> Cards(IEnumerable<string>? urls)
    {
        if (urls == null)
            yield break;

        int count = 0;

        foreach (string url in urls)
        {
            ModVideo? card = Card(url);

            if (card == null)
                continue;

            yield return card;

            if (++count == 6)
                yield break;
        }
    }

    public static ModVideo? Card(string url)
    {
        if (!Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out Uri? uri))
            return null;

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            return null;

        string? id = YouTubeId(uri);

        if (id != null)
            return new ModVideo(uri.AbsoluteUri, id, $"https://i.ytimg.com/vi/{id}/hqdefault.jpg", "YouTube");

        return new ModVideo(uri.AbsoluteUri, null, null, uri.Host);
    }

    public static string? YouTubeId(Uri uri)
    {
        string host = uri.IdnHost;

        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            host = host.Substring(4);

        if (host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
            return TakeId(uri.AbsolutePath.Trim('/'));

        if (!host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase)
            && !host.Equals("m.youtube.com", StringComparison.OrdinalIgnoreCase)
            && !host.Equals("music.youtube.com", StringComparison.OrdinalIgnoreCase)
            && !host.Equals("youtube-nocookie.com", StringComparison.OrdinalIgnoreCase))
            return null;

        string path = uri.AbsolutePath;

        if (path.Equals("/watch", StringComparison.OrdinalIgnoreCase))
            return TakeId(QueryValue(uri.Query, "v"));

        if (path.StartsWith("/embed/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/shorts/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/live/", StringComparison.OrdinalIgnoreCase))
            return TakeId(path.Substring(path.IndexOf('/', 1) + 1));

        return null;
    }

    private static string? QueryValue(string query, string name)
    {
        foreach (string part in query.TrimStart('?').Split('&'))
        {
            string[] pair = part.Split('=', 2);

            if (pair.Length == 2 && pair[0].Equals(name, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(pair[1]);
        }

        return null;
    }

    private static string? TakeId(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        string id = value.Split('/', '?', '&')[0];
        return YouTubeIdPattern.IsMatch(id) ? id : null;
    }
}
