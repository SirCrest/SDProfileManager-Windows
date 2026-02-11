using System.Text.Json.Serialization;

namespace SDProfileManager.Models;

public class PagesManifest
{
    [JsonPropertyName("Current")]
    public string? Current { get; set; }

    [JsonPropertyName("Default")]
    public string? Default { get; set; }

    [JsonPropertyName("Pages")]
    public List<string>? Pages { get; set; }
}
