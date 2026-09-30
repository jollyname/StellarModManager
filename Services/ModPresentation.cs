using Avalonia.Media;
using Avalonia.Media.Immutable;
using System;

namespace StellarModManager.Services;

/// <summary>
/// Shared card presentation for installed and repository mods: the letter/name
/// fallback tile and the Modrinth category palette, so a mod looks identical
/// whether it is in your library or still in the repository.
/// </summary>
public static class ModPresentation
{
    // Modrinth's category ramp: green performance, blue content, orange utility.
    private static readonly string[][] Categories =
    {
        new[] { "Performance", "#1bd96a", "perf", "boost", "fps", "optimi", "faster", "speed", "tweak", "performance", "efficien", "memory", "lag" },
        new[] { "Visual", "#a55eff", "graphic", "shader", "visual", "texture", "model", "skin", "hd ", "quality", "render", "lighting", "resolut" },
        new[] { "Content", "#3394ff", "quest", "item", "npc", "map", "world", "expansion", "dlc", "chapter", "story", "mob", "boss", "biome", "creature", "weapon", "armor", "relic" },
        new[] { "Fixes", "#ff496e", "fix", "bug", "crash", "patch", "compat", "repair" },
        new[] { "Utility", "#ffa800", "util", "tool", "helper", "keybind", "manager", "ui ", "interface", "config" }
    };

    private static readonly string[] AvatarPalette =
    {
        "#7b2fbf", "#4c1d95", "#2e7d6b", "#b4531f",
        "#1d5fa8", "#8e2f5e", "#3f6b1f", "#5b4bc4"
    };

    public static string Initial(string? name)
    {
        string trimmed = name?.Trim() ?? "";

        return trimmed.Length == 0 ? "?" : trimmed[..1].ToUpperInvariant();
    }

    public static IBrush AvatarBrush(string? seed)
    {
        unchecked
        {
            int hash = 17;

            foreach (char c in seed ?? "")
                hash = hash * 31 + c;

            return new ImmutableSolidColorBrush(Color.Parse(AvatarPalette[(hash & int.MaxValue) % AvatarPalette.Length]));
        }
    }

    public static (string Name, string Color) Category(string? name, string? description)
    {
        string haystack = $"{name} {description}".ToLowerInvariant();

        foreach (string[] category in Categories)
        {
            for (int i = 2; i < category.Length; i++)
            {
                if (haystack.Contains(category[i], StringComparison.Ordinal))
                    return (category[0], category[1]);
            }
        }

        return (Categories[^1][0], Categories[^1][1]);
    }

    public static IBrush ChipText { get; } = new ImmutableSolidColorBrush(Color.Parse("#141518"));
}
