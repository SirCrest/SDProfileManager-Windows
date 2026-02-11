namespace SDProfileManager.Models;

public class TouchStripLayoutModel
{
    public string Id { get; set; } = string.Empty;
    public List<TouchStripLayoutItem> Items { get; set; } = [];
}

public class TouchStripLayoutItem
{
    public string Key { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public TouchStripRect Rect { get; set; } = new();
    public int ZOrder { get; set; }
    public string? Value { get; set; }
    public bool Enabled { get; set; } = true;
    public TouchStripFontSpec? Font { get; set; }
    public string? Alignment { get; set; }
    public string? Background { get; set; }
}

public class TouchStripRect
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public class TouchStripFontSpec
{
    public double? Size { get; set; }
    public int? Weight { get; set; }
}
