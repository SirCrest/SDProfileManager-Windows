using SDProfileManager.Models;

namespace SDProfileManager.Tests.Models;

public class ProfileTemplateLayoutExtensionsTests
{
    [Fact]
    public void GalleonTemplate_UsesTwoByTwoTouchStripMapping()
    {
        var template = ProfileTemplates.ById["g100sd"];

        Assert.True(template.HasTouchStrip());
        Assert.False(template.HasDialSlots());
        Assert.Equal(2, template.GetTouchStripRows());
        Assert.Equal(2, template.GetTouchStripColumns());
        Assert.Equal(2, template.GetEncoderRows());

        Assert.Equal(0, template.GetEncoderColumnForTouchStripCell(0, 0));
        Assert.Equal(0, template.GetEncoderRowForTouchStripCell(0, 0));
        Assert.Equal(0, template.GetEncoderColumnForTouchStripCell(0, 1));
        Assert.Equal(1, template.GetEncoderRowForTouchStripCell(0, 1));

        Assert.Equal(1, template.GetEncoderColumnForTouchStripCell(1, 0));
        Assert.Equal(0, template.GetEncoderRowForTouchStripCell(1, 0));
        Assert.Equal(1, template.GetEncoderColumnForTouchStripCell(1, 1));
        Assert.Equal(1, template.GetEncoderRowForTouchStripCell(1, 1));
    }

    [Fact]
    public void StudioTemplate_HasDialSlotsWithoutTouchStrip()
    {
        var template = ProfileTemplates.ById["sdstudio"];

        Assert.False(template.HasTouchStrip());
        Assert.True(template.HasDialSlots());
        Assert.Equal(0, template.GetTouchStripRows());
        Assert.Equal(0, template.GetTouchStripColumns());
        Assert.Equal(1, template.GetEncoderRows());
    }

    [Fact]
    public void MiniTemplate_ReportsNoEncoderRows()
    {
        var template = ProfileTemplates.ById["mini"];

        Assert.False(template.HasTouchStrip());
        Assert.False(template.HasDialSlots());
        Assert.Equal(0, template.GetEncoderRows());
    }
}
