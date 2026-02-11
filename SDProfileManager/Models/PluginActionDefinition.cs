namespace SDProfileManager.Models;

public class PluginActionDefinition
{
    public PluginRenderAvailability Availability { get; set; } = PluginRenderAvailability.PluginMissing;
    public string PluginUuid { get; set; } = string.Empty;
    public string? PluginFolderPath { get; set; }
    public string? LayoutPath { get; set; }
    public string? EncoderIconPath { get; set; }
    public string Message { get; set; } = string.Empty;
}
