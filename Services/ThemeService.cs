using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
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
        resources["Theme.WindowBackgroundBrush"] = new SolidColorBrush(Color.Parse(theme.WindowBackground));
        resources["Theme.TitleBarBackgroundBrush"] = new SolidColorBrush(Color.Parse(theme.TitleBarBackground));
        resources["Theme.SurfaceBackgroundBrush"] = new SolidColorBrush(Color.Parse(theme.SurfaceBackground));
        resources["Theme.DetailBackgroundBrush"] = new SolidColorBrush(Color.Parse(theme.DetailBackground));
        resources["Theme.ImagePanelBrush"] = new SolidColorBrush(Color.Parse(theme.ImagePanel));
        resources["Theme.CardImageBrush"] = new SolidColorBrush(Color.Parse(theme.CardImage));
        resources["Theme.CardImageOverlayBrush"] = new SolidColorBrush(Color.Parse(theme.CardImageOverlay));
        resources["Theme.SetupPanelBrush"] = new SolidColorBrush(Color.Parse(theme.SetupPanelBackground));
        resources["Theme.BorderSubtleBrush"] = new SolidColorBrush(Color.Parse(theme.BorderSubtle));
        resources["Theme.ChipNeutralBrush"] = new SolidColorBrush(Color.Parse(theme.ChipNeutral));
        resources["Theme.ChipAccentBackgroundBrush"] = new SolidColorBrush(Color.Parse(theme.ChipAccentBackground));
        resources["Theme.ChipAccentTextBrush"] = new SolidColorBrush(Color.Parse(theme.ChipAccentText));
        resources["Theme.InstalledBadgeBrush"] = new SolidColorBrush(Color.Parse(theme.InstalledBadge));
        resources["Theme.IconGlyphBrush"] = new SolidColorBrush(Color.Parse(theme.IconGlyph));
        resources["Theme.TextOnAccentBrush"] = new SolidColorBrush(Color.Parse(theme.TextOnAccent));
        resources["Theme.AccentBrightBrush"] = new SolidColorBrush(Color.Parse(theme.AccentBright));
        resources["Theme.AccentBrightPressedBrush"] = new SolidColorBrush(Color.Parse(theme.AccentBrightPressed));
        resources["Theme.AccentBrightDisabledBrush"] = new SolidColorBrush(Color.Parse(theme.AccentBrightDisabled));
        resources["Theme.SubtleFillBrush"] = new SolidColorBrush(Color.Parse(theme.SubtleFill));
        resources["Theme.SubtleFillHoverBrush"] = new SolidColorBrush(Color.Parse(theme.SubtleFillHover));
        resources["Theme.ChipAccentHoverBrush"] = new SolidColorBrush(Color.Parse(theme.ChipAccentHover));
        resources["Theme.ChipNeutralHoverBrush"] = new SolidColorBrush(Color.Parse(theme.ChipNeutralHover));
        resources["Theme.ToggleKnobBrush"] = new SolidColorBrush(Color.Parse(theme.ToggleKnob));
        resources["Theme.ToggleKnobDisabledBrush"] = new SolidColorBrush(Color.Parse(theme.ToggleKnobDisabled));
        resources["Theme.DisabledFillBrush"] = new SolidColorBrush(Color.Parse(theme.DisabledFill));

        // Decide light/dark from the actual window colour, not the theme's name, so
        // every named theme (Ocean, Slate, ...) resolves to the right variant + logo.
        bool isLight = IsLightSurface(theme.WindowBackground);
        resources["Theme.IsLight"] = isLight;
        resources["Theme.IsDark"] = !isLight;

        Application.Current.RequestedThemeVariant =
            isLight ? ThemeVariant.Light : ThemeVariant.Dark;
    }

    private static bool IsLightSurface(string hex)
    {
        if (!Color.TryParse(hex, out Color color))
            return true;

        double luminance = ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255.0;
        return luminance > 0.5;
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