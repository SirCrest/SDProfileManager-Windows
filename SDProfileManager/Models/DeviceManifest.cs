using System.Text.Json.Serialization;

namespace SDProfileManager.Models;

public class DeviceManifest
{
    [JsonPropertyName("Model")]
    public string? Model { get; set; }

    [JsonPropertyName("UUID")]
    public string? UUID { get; set; }
}
