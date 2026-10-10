namespace StellarModManager.Models;

public class ThemeDefinition
{
    public string Name { get; set; } = "Default";

    // Sidebar and title bar
    public string Background { get; set; } = "#202020";
    // Main page area
    public string Content { get; set; } = "#282828";
    // Details pane
    public string Panel { get; set; } = "#1C1C1C";
    // Rows, inputs, cards
    public string Surface { get; set; } = "#2F2F2F";
    public string SurfaceHover { get; set; } = "#383838";
    public string Border { get; set; } = "#3A3A3A";

    public string Accent { get; set; } = "#60CDFF";
    public string AccentHover { get; set; } = "#4CC2FF";
    public string AccentPressed { get; set; } = "#3AA7DD";
    public string AccentForeground { get; set; } = "#000000";

    public string TextPrimary { get; set; } = "#FFFFFF";
    public string TextSecondary { get; set; } = "#C5C5C5";
    public string TextMuted { get; set; } = "#8A8A8A";

    public string Success { get; set; } = "#6CCB5F";

    public string Danger { get; set; } = "#C42B1C";
    public string DangerHover { get; set; } = "#B0261A";
    public string DangerPressed { get; set; } = "#9A2016";
}
