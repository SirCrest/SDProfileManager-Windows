using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SDProfileManager.Models;

public class PackageManifest
{
    [JsonPropertyName("AppVersion")]
    public string? AppVersion { get; set; }

    [JsonPropertyName("DeviceModel")]
    public string? DeviceModel { get; set; }

    [JsonPropertyName("DeviceSettings")]
    public JsonNode? DeviceSettings { get; set; }

    [JsonPropertyName("FormatVersion")]
    public int? FormatVersion { get; set; }

    [JsonPropertyName("OSType")]
    public string? OSType { get; set; }

    [JsonPropertyName("OSVersion")]
    public string? OSVersion { get; set; }

    [JsonPropertyName("RequiredPlugins")]
    public List<string>? RequiredPlugins { get; set; }
}
