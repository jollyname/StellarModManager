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
        resources["Theme.WindowGradientStart"] = Color.Parse(theme.WindowGradientStart);
        resources["Theme.WindowGradientMid"] = Color.Parse(theme.WindowGradientMid);
        resources["Theme.WindowGradientEnd"] = Color.Parse(theme.WindowGradientEnd);
        resources["Theme.AccentStart"] = Color.Parse(theme.AccentStart);
        resources["Theme.AccentEnd"] = Color.Parse(theme.AccentEnd);
        resources["Theme.AccentSolidBrush"] = new SolidColorBrush(Color.Parse(theme.AccentStart));
        resources["Theme.AccentEndBrush"] = new SolidColorBrush(Color.Parse(theme.AccentEnd));
        resources["Theme.AccentHoverBrush"] = new SolidColorBrush(Color.Parse(theme.AccentHover));
        resources["Theme.AccentPressedBrush"] = new SolidColorBrush(Color.Parse(theme.AccentPressed));
        resources["Theme.CardBackgroundBrush"] = new SolidColorBrush(Color.Parse(theme.CardBackground));
        resources["Theme.ModCardBackgroundBrush"] = new SolidColorBrush(Color.Parse(theme.ModCardBackground));
        resources["Theme.ModCardBorderBrush"] = new SolidColorBrush(Color.Parse(theme.ModCardBorder));
        resources["Theme.TextPrimaryBrush"] = new SolidColorBrush(Color.Parse(theme.TextPrimary));
        resources["Theme.TextSecondaryBrush"] = new SolidColorBrush(Color.Parse(theme.TextSecondary));
        resources["Theme.TextMutedBrush"] = new SolidColorBrush(Color.Parse(theme.TextMuted));
        resources["Theme.DangerBrush"] = new SolidColorBrush(Color.Parse(theme.Danger));
        resources["Theme.DangerHoverBrush"] = new SolidColorBrush(Color.Parse(theme.DangerHover));
        resources["Theme.DangerPressedBrush"] = new SolidColorBrush(Color.Parse(theme.DangerPressed));
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