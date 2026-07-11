using System;

namespace StellarModManager.Models;

public record UpdateInfo(Version Version, GitHubAsset[] Assets, string ReleaseNotes);
public record GitHubRelease(string tag_name, GitHubAsset[] assets, string body);
public record GitHubAsset(string name, string browser_download_url, long size);