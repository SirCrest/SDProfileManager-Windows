using SDProfileManager.Models;

namespace SDProfileManager.Tests.Models;

public class ProfileTemplatesTests
{
    [Theory]
    [InlineData("20GAT9902")]
    [InlineData("20gat9902")]
    [InlineData("20GAT9901")]
    [InlineData("20GAT-UNKNOWN")]
    public void GetTemplate_StreamDeckXlVariants_MapToSdxl(string deviceModel)
    {
        var template = ProfileTemplates.GetTemplate(deviceModel);

        Assert.Equal("sdxl", template.Id);
        Assert.Equal(8, template.Columns);
        Assert.Equal(4, template.Rows);
        Assert.Equal(0, template.Dials);
    }

    [Fact]
    public void GetTemplate_UnknownModel_UsesConservativeXlFallback()
    {
        var template = ProfileTemplates.GetTemplate("UNKNOWN-MODEL");

        Assert.Equal("sdxl", template.Id);
        Assert.Equal(0, template.Dials);
    }
}
