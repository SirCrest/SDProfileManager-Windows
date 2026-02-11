using System.Text.Json.Serialization;

namespace SDProfileManager.Models;

public class PageManifest
{
    [JsonPropertyName("Controllers")]
    public List<ControllerManifest> Controllers { get; set; } = [];

    [JsonPropertyName("Icon")]
    public string Icon { get; set; } = "";

    [JsonPropertyName("Name")]
    public string Name { get; set; } = "";
}
