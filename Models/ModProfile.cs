using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;

namespace StellarModManager.Models;

public partial class ModProfile : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? SnapshotId { get; set; }

    public List<string> DeployedModIds { get; set; } = [];

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    public partial string EditName { get; set; } = "";
}
