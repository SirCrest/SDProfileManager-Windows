using System.Text.Json.Serialization;

namespace SDProfileManager.Models;

public class RootProfileManifest
{
    [JsonPropertyName("Device")]
    public DeviceManifest? Device { get; set; }

    [JsonPropertyName("Name")]
    public string? Name { get; set; }

    [JsonPropertyName("Pages")]
    public PagesManifest? Pages { get; set; }

    [JsonPropertyName("Version")]
    public string? Version { get; set; }
}
