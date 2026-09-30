namespace StellarModManager.Models;

public class ThemeDefinition
{
    public string Name { get; set; } = "Dark";

    public string WindowGradientStart { get; set; } = "#1A1A1A";
    public string WindowGradientMid { get; set; } = "#1A1A1A";
    public string WindowGradientEnd { get; set; } = "#1A1A1A";

    public string AccentStart { get; set; } = "#2E1559";
    public string AccentEnd { get; set; } = "#9B59B6";
    public string AccentHover { get; set; } = "#3A1C72";
    public string AccentPressed { get; set; } = "#241046";

    public string CardBackground { get; set; } = "#242527";
    public string ModCardBackground { get; set; } = "#1F2022";
    public string ModCardBorder { get; set; } = "#2E3033";

    public string TextPrimary { get; set; } = "#E8E8EA";
    public string TextSecondary { get; set; } = "#A8A8AE";
    public string TextMuted { get; set; } = "#7A7A80";

    public string Danger { get; set; } = "#E5484D";
    public string DangerHover { get; set; } = "#C93A3F";
    public string DangerPressed { get; set; } = "#A82F34";

    // Surfaces the views used to hardcode as dark-only hex values.
    public string WindowBackground { get; set; } = "#141518";
    public string TitleBarBackground { get; set; } = "#1A1B20";
    public string SurfaceBackground { get; set; } = "#1A1B1F";
    public string DetailBackground { get; set; } = "#0E0F12";
    public string ImagePanel { get; set; } = "#15161A";
    public string CardImage { get; set; } = "#151517";
    public string CardImageOverlay { get; set; } = "#1A1A1D";
    public string SetupPanelBackground { get; set; } = "#1E1F24";
    public string BorderSubtle { get; set; } = "#2E3033";
    public string ChipNeutral { get; set; } = "#3A3C42";
    public string ChipAccentBackground { get; set; } = "#2E2A3C";
    public string ChipAccentText { get; set; } = "#C79BFF";
    public string InstalledBadge { get; set; } = "#7B2FBF";
    public string IconGlyph { get; set; } = "#8A8D94";
    public string TextOnAccent { get; set; } = "#FFFFFF";

    // Interactive chrome that used to be hardcoded in AppStyles.axaml.
    public string AccentBright { get; set; } = "#8E42D2";
    public string AccentBrightPressed { get; set; } = "#6B27A8";
    public string AccentBrightDisabled { get; set; } = "#332B3D";
    public string SubtleFill { get; set; } = "#26272C";
    public string SubtleFillHover { get; set; } = "#242427";
    public string ChipAccentHover { get; set; } = "#4A3A5E";
    public string ChipNeutralHover { get; set; } = "#4A4C53";
    public string ToggleKnob { get; set; } = "#C8CAD0";
    public string ToggleKnobDisabled { get; set; } = "#6E7076";
    public string DisabledFill { get; set; } = "#2A2C31";
}
