namespace SDProfileManager.Models;

public static class ProfileTemplateLayoutExtensions
{
    public static int GetEncoderRows(this ProfileTemplate template)
    {
        if (template.Dials <= 0 && !template.HasTouchStrip())
            return 0;

        // Most devices expose a single encoder-action row. Galleon uses two.
        return Math.Max(1, template.GetTouchStripRows());
    }

    public static bool HasTouchStrip(this ProfileTemplate template) => template.Id switch
    {
        // Studio has encoder knobs only (no touch strip).
        "sdstudio" => false,
        // Galleon exposes a touch strip area but no round dial controls in UI.
        "g100sd" => true,
        _ => template.Dials > 0
    };

    public static bool HasDialSlots(this ProfileTemplate template) => template.Id switch
    {
        // Galleon uses strip UI only for encoder controls.
        "g100sd" => false,
        _ => template.Dials > 0
    };

    public static bool IsTouchStripAboveKeys(this ProfileTemplate template) => template.Id switch
    {
        "g100sd" => true,
        _ => false
    };

    public static int GetTouchStripRows(this ProfileTemplate template)
    {
        if (!template.HasTouchStrip())
            return 0;

        return template.Id switch
        {
            "g100sd" => 2,
            _ => 1
        };
    }

    public static int GetTouchStripColumns(this ProfileTemplate template)
    {
        if (!template.HasTouchStrip())
            return 0;

        return template.Id switch
        {
            "g100sd" => 2,
            _ => Math.Max(template.Dials, 1)
        };
    }

    public static int GetEncoderColumnForTouchStripCell(this ProfileTemplate template, int column, int row)
    {
        if (!template.HasTouchStrip())
            return Math.Max(column, 0);

        return template.Id switch
        {
            // Two stacked segments map to each physical dial.
            "g100sd" => Math.Clamp(column, 0, Math.Max(template.Dials - 1, 0)),
            _ => Math.Clamp(column, 0, Math.Max(template.Dials - 1, 0))
        };
    }

    public static int GetEncoderRowForTouchStripCell(this ProfileTemplate template, int column, int row)
    {
        var encoderRows = Math.Max(template.GetEncoderRows(), 1);
        if (!template.HasTouchStrip())
            return 0;

        return template.Id switch
        {
            // Top strip segment = y0, bottom strip segment = y1.
            "g100sd" => Math.Clamp(row, 0, encoderRows - 1),
            _ => 0
        };
    }
}
