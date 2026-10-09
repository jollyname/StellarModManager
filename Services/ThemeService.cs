using Avalonia;
using Avalonia.Media;
using StellarModManager.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace StellarModManager.Services;

public class ThemeService
{
    private readonly string themesFolder = Path.Combine(AppContext.BaseDirectory, "Data", "Themes");
    private Dictionary<string, ThemeDefinition>? _themeCache;

    public void ApplyTheme(ThemeDefinition theme)
    {
        var resources = Application.Current!.Resources;

        void SetBrush(string key, string hex) => resources[$"Theme.{key}Brush"] = new SolidColorBrush(Color.Parse(hex));

        SetBrush("Background", theme.Background);
        SetBrush("Content", theme.Content);
        SetBrush("Panel", theme.Panel);
        SetBrush("Surface", theme.Surface);
        SetBrush("SurfaceHover", theme.SurfaceHover);
        SetBrush("Border", theme.Border);
        SetBrush("Accent", theme.Accent);
        SetBrush("AccentHover", theme.AccentHover);
        SetBrush("AccentPressed", theme.AccentPressed);
        SetBrush("AccentForeground", theme.AccentForeground);
        SetBrush("TextPrimary", theme.TextPrimary);
        SetBrush("TextSecondary", theme.TextSecondary);
        SetBrush("TextMuted", theme.TextMuted);
        SetBrush("Success", theme.Success);
        SetBrush("Danger", theme.Danger);
        SetBrush("DangerHover", theme.DangerHover);
        SetBrush("DangerPressed", theme.DangerPressed);

        // Fluent controls (checkboxes, focus rings, text selection) follow the theme accent.
        var accent = Color.Parse(theme.Accent);
        foreach (var key in new[] { "SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
                                    "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3" })
        {
            resources[key] = accent;
        }
    }

    private Dictionary<string, ThemeDefinition> LoadAllThemes()
    {
        if (_themeCache != null)
            return _themeCache;

        _themeCache = new Dictionary<string, ThemeDefinition>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(themesFolder))
            return _themeCache;

        foreach (var path in Directory.GetFiles(themesFolder, "*.json"))
        {
            try
            {
                string json = File.ReadAllText(path);
                var theme = JsonSerializer.Deserialize<ThemeDefinition>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (theme != null && !string.IsNullOrWhiteSpace(theme.Name)) _themeCache[theme.Name] = theme;
            }
            catch
            {
                // skip bad file
            }
        }

        return _themeCache;
    }

    public string[] GetAvailableThemeNames() => LoadAllThemes().Keys.ToArray();

    public ThemeDefinition LoadTheme(string themeName)
    {
        var themes = LoadAllThemes();
        return themes.TryGetValue(themeName, out var theme) ? theme : new ThemeDefinition();
    }
}