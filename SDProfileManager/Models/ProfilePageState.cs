using System.Text.Json.Nodes;

namespace SDProfileManager.Models;

public class ProfilePageState
{
    public string Id { get; set; } = "";
    public PageManifest Manifest { get; set; } = new();
    public Dictionary<string, JsonNode> KeypadActions { get; set; } = [];
    public Dictionary<string, JsonNode> EncoderActions { get; set; } = [];
}
