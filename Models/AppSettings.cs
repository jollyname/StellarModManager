using System.Globalization;

public class AppSettings
{
    public string Language { get; set; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
    public bool ConfirmBeforeRemove { get; set; } = true;
    public bool AutoCheckForModUpdates { get; set; } = true;
    public bool AutoCheckForAppUpdates { get; set; } = true;
    public string Theme { get; set; } = "Purple";
}