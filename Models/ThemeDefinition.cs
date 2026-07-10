namespace StellarModManager.Models;

public class ThemeDefinition
{
    public string Name { get; set; } = "Default";

    public string WindowGradientStart { get; set; } = "#24104F";
    public string WindowGradientMid { get; set; } = "#5B2C83";
    public string WindowGradientEnd { get; set; } = "#9B59B6";

    public string AccentStart { get; set; } = "#5B2C83";
    public string AccentEnd { get; set; } = "#9B59B6";
    public string AccentHover { get; set; } = "#4A2369";
    public string AccentPressed { get; set; } = "#3A1B52";

    public string CardBackground { get; set; } = "#F7F5FA";
    public string ModCardBackground { get; set; } = "#FAF8FC";
    public string ModCardBorder { get; set; } = "#EAE3F0";

    public string TextPrimary { get; set; } = "#2B1B3D";
    public string TextSecondary { get; set; } = "#6B5C7A";
    public string TextMuted { get; set; } = "#8A7F91";

    public string Danger { get; set; } = "#C0392B";
    public string DangerHover { get; set; } = "#A5281A";
    public string DangerPressed { get; set; } = "#7F1D12";
}