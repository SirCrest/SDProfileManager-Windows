namespace SDProfileManager.Models;

public enum TouchStripRenderMode
{
    Layout,
    Fallback
}

public class TouchStripRenderModel
{
    public TouchStripRenderMode Mode { get; set; } = TouchStripRenderMode.Fallback;
    public PluginRenderAvailability Availability { get; set; } = PluginRenderAvailability.ProfileOnly;
    public string BadgeText { get; set; } = string.Empty;
    public string TooltipText { get; set; } = string.Empty;
    public List<TouchStripRenderLayer> Layers { get; set; } = [];
    public string PrimaryLabel { get; set; } = string.Empty;
    public string SecondaryLabel { get; set; } = string.Empty;
    public string? PrimaryIconPath { get; set; }
}

public class TouchStripRenderLayer
{
    public string Key { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "image", "text", "placeholder"
    public TouchStripRect Rect { get; set; } = new();
    public int ZOrder { get; set; }
    public string? ImagePath { get; set; }
    public string? Text { get; set; }
    public string? Alignment { get; set; }
    public double? FontSize { get; set; }
    public int? FontWeight { get; set; }
    public string? Background { get; set; }
}
