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
    }
}
