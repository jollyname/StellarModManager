namespace StellarModManager.Models;

/// <summary>
/// One label/value line in the details panel. Built by the view model from whatever
/// fields the mod actually has, so the panel can adapt to installed vs online entries
/// instead of hard-coding a fixed set of rows.
/// </summary>
public class DetailInfoRow
{
    public DetailInfoRow(string label, string value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }

    public string Value { get; }
}

/// <summary>
/// One entry on the version timeline. <see cref="IsLast"/> lets the template drop the
/// connector below the final dot so the rail never dangles past the last version.
/// </summary>
public class VersionTimelineEntry
{
    public VersionTimelineEntry(string version, string notes, bool isLast)
    {
        Version = version;
        Notes = notes;
        IsLast = isLast;
    }

    public string Version { get; }

    public string Notes { get; }

    public bool IsLast { get; }
}
