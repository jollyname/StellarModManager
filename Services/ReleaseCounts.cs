using StellarModManager.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;

namespace StellarModManager.Services;

public static class ReleaseCounts
{
    public static long Total(IReadOnlyList<GitHubRelease> releases)
    {
        long sum = 0;

        foreach (GitHubRelease release in releases)
        {
            if (release.assets == null)
                continue;

            foreach (GitHubAsset asset in release.assets)
                sum += asset.download_count;
        }

        return sum;
    }

    // Empty list must not throw. Enumerable.Max does, and that used to drop the whole count.
    public static DateTime? Latest(IReadOnlyList<GitHubRelease> releases)
    {
        DateTime? latest = null;

        foreach (GitHubRelease release in releases)
        {
            if (release.published_at is DateTime at && (latest == null || at > latest))
                latest = at;
        }

        return latest;
    }

    public static bool IsLimited(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            return true;

        if (response.StatusCode != HttpStatusCode.Forbidden)
            return false;

        if (response.Headers.RetryAfter != null)
            return true;

        return response.Headers.TryGetValues("X-RateLimit-Remaining", out IEnumerable<string>? remaining)
            && remaining.FirstOrDefault() == "0";
    }

    public static DateTime BlockedUntil(HttpResponseMessage response, DateTime utcNow)
    {
        if (response.Headers.TryGetValues("X-RateLimit-Reset", out IEnumerable<string>? reset)
            && long.TryParse(reset.FirstOrDefault(), out long unix))
        {
            DateTime until = DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;
            return until > utcNow ? until : utcNow.AddMinutes(1);
        }

        if (response.Headers.RetryAfter?.Delta is TimeSpan wait && wait > TimeSpan.Zero)
            return utcNow + wait;

        return utcNow.AddMinutes(15);
    }
}
