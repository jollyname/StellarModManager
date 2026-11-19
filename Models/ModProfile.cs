using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace StellarModManager.Models;

public partial class ModProfile : ObservableObject
{
    [ObservableProperty]
    private string name = "";

    public List<string> DeployedModIds { get; set; } = new();

    [JsonIgnore]
    [ObservableProperty]
    private bool isActive;

    [JsonIgnore]
    [ObservableProperty]
    private bool isEditing;

    [JsonIgnore]
    [ObservableProperty]
    private string editName = "";
}
