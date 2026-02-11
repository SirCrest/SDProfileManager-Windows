using System.Text.Json.Nodes;

namespace SDProfileManager.Models;

public class ProfileArchiveSnapshot
{
    public string? SourcePath { get; set; }
    public string ExtractedRootPath { get; set; } = "";
    public string PresetId { get; set; } = "";
    public string Name { get; set; } = "";
    public string ProfileRootName { get; set; } = "";
    public string ActivePageId { get; set; } = "";
    public List<string> PageOrder { get; set; } = [];
    public Dictionary<string, ProfilePageStateSnapshot> PageStates { get; set; } = [];
    public string PackageManifestJson { get; set; } = "{}";
    public string ProfileManifestJson { get; set; } = "{}";
}

public class ProfilePageStateSnapshot
{
    public string Id { get; set; } = "";
    public string ManifestJson { get; set; } = "{}";
    public Dictionary<string, string> KeypadActionsJson { get; set; } = [];
    public Dictionary<string, string> EncoderActionsJson { get; set; } = [];
}
