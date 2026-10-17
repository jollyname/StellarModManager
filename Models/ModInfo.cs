using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic; // Lists
using System.Text.Json.Serialization;

namespace StellarModManager.Models
{
    public class ModInfo : ObservableObject
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("author")]
        public string Author { get; set; } = "";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("thumbnail")]
        public string Thumbnail { get; set; } = "";

        [JsonPropertyName("images")]
        public List<string> Images { get; set; } = new();

        [JsonPropertyName("videos")]
        public List<string> Videos { get; set; } = new();

        [JsonPropertyName("ai")]
        public string Ai { get; set; } = "";

        // "active" or "inactive". Empty means the author did not say.
        [JsonPropertyName("development")]
        public string Development { get; set; } = "";

        [JsonPropertyName("tags")]
        public List<string> Tags { get; set; } = new();
    }
}
