using System.Text.RegularExpressions;

namespace StellarModManager.Services;

// raw.githubusercontent.com caches /main for five minutes and ignores ?t=.
// The commit feed does not, and a raw URL that names the commit is that exact file.
public static class GithubFiles
{
    private static readonly Regex CommitInFeed = new(
        @"Grit::Commit/([0-9a-f]{40})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string? CommitFromAtom(string atom)
    {
        if (string.IsNullOrEmpty(atom))
            return null;

        Match match = CommitInFeed.Match(atom);
        return match.Success ? match.Groups[1].Value : null;
    }

    public static string RawFile(string owner, string repo, string commit, string metadataPath, string fileName)
    {
        string folder = (metadataPath ?? "").Trim('/');
        string prefix = folder.Length == 0 ? "" : folder + "/";
        return $"https://raw.githubusercontent.com/{owner}/{repo}/{commit}/{prefix}{fileName}";
    }
}
