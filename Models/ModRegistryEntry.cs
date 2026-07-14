using System.Text.Json.Serialization;

namespace StellarModManager.Models;

public class ModRegistryEntry
{
    [JsonPropertyName("author")]
    public string Author { get; set; } = "";

    [JsonPropertyName("repo")]
    public string Repo { get; set; } = "";

    [JsonPropertyName("metadata")]
    public string MetadataPath { get; set; } = "";
}