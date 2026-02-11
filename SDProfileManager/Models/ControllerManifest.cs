using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SDProfileManager.Models;

public class ControllerManifest
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("Actions")]
    public Dictionary<string, JsonNode>? Actions { get; set; }

    [JsonPropertyName("Type")]
    public string Type { get; set; } = "Keypad";
}
