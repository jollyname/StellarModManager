using Avalonia.Media.Imaging;
using StellarModManager.Models;
using System;
using System.Collections.Generic;
using System.IO;

namespace StellarModManager.Services;

// Installed mods keep their artwork next to mod.json inside Library/<id>/.
// Only one decode is ever attempted per mod so virtualization can recycle
// cards without re-reading files from disk.
public static class ModIconLoader
{
    private static readonly string[] IconCandidates =
    {
        "", "icon.png", "thumbnail.png", "logo.png", "cover.png", "image.png"
    };

    public static void TryLoad(InstalledModInfo mod)
    {
        if (mod.IconLoadAttempted || mod.IconImage is not null)
            return;

        mod.IconLoadAttempted = true;

        if (string.IsNullOrWhiteSpace(mod.LibraryFolder) || !Directory.Exists(mod.LibraryFolder))
            return;

        string root = Path.GetFullPath(mod.LibraryFolder);

        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(mod.Thumbnail))
            candidates.Add(mod.Thumbnail);

        candidates.AddRange(IconCandidates);

        foreach (string relative in candidates)
        {
            if (string.IsNullOrWhiteSpace(relative))
                continue;

            string full;

            try
            {
                full = Path.GetFullPath(Path.Combine(root, relative));
            }
            catch
            {
                continue;
            }

            // never let metadata escape the mod folder
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!File.Exists(full))
                continue;

            try
            {
                using FileStream stream = File.OpenRead(full);
                mod.IconImage = Bitmap.DecodeToWidth(stream, 160);
                return;
            }
            catch
            {
                // not a decodable image, try the next candidate
            }
        }
    }
}
