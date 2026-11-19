using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Text.Json.Serialization;

namespace StellarModManager.Models;

public partial class ModProfile : ObservableObject
{
    public Guid Id { get; set; }

    [ObservableProperty]
    private string name = "";

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
