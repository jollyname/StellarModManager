using System;
using System.Collections.Generic;

namespace StellarModManager.Models;

internal sealed class GameModProfiles
{
    public Guid ActiveProfileId { get; set; }
    public Guid? LastOperationId { get; set; }
    public List<ModProfile> Profiles { get; set; } = new();
    public List<ProfileMod> InstalledMods { get; set; } = new();
}

internal sealed record ProfileMod(InstalledModInfo Mod, List<string> Files);
