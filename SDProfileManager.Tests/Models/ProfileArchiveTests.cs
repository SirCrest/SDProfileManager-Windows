using System.Text.Json.Nodes;
using SDProfileManager.Models;
using SDProfileManager.Tests.TestData;

namespace SDProfileManager.Tests.Models;

public class ProfileArchiveTests
{
    [Fact]
    public void RemovePage_ReturnsFalseForMissingPage()
    {
        var profile = ProfileFactory.Create(pageIds: ["page-a", "page-b"]);

        var removed = profile.RemovePage("missing-page");

        Assert.False(removed);
        Assert.NotNull(profile.GetPageState("page-a"));
        Assert.NotNull(profile.GetPageState("page-b"));
    }

    [Fact]
    public void RemovePage_PreventsDeletingLastVisiblePage()
    {
        var profile = ProfileFactory.Create(pageIds: ["only-page"]);

        var removed = profile.RemovePage("only-page");

        Assert.False(removed);
        Assert.NotNull(profile.GetPageState("only-page"));
    }

    [Fact]
    public void RemovePage_UpdatesActivePageAndOrder()
    {
        var profile = ProfileFactory.Create(pageIds: ["page-a", "page-b", "page-c"]);
        profile.SetActivePage("page-b");

        var removed = profile.RemovePage("page-b");

        Assert.True(removed);
        Assert.Null(profile.GetPageState("page-b"));
        Assert.DoesNotContain("page-b", profile.PageOrder, StringComparer.OrdinalIgnoreCase);
        Assert.True(string.Equals(profile.ActivePageId, "page-a", StringComparison.OrdinalIgnoreCase)
            || string.Equals(profile.ActivePageId, "page-c", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SetAction_UsesNormalizedPageIds()
    {
        var profile = ProfileFactory.Create(pageIds: ["Page-A", "Page-B"]);
        var action = JsonNode.Parse("""
            {
              "Name": "Action",
              "UUID": "com.test.action",
              "Plugin": { "UUID": "com.test.plugin" }
            }
            """)!;

        profile.SetAction(action, ControllerKind.Keypad, "0,0", "PAGE-A");
        var found = profile.GetAction(ControllerKind.Keypad, "0,0", "page-a");

        Assert.NotNull(found);
        Assert.Equal("com.test.action", found!["UUID"]?.GetValue<string>());
    }
}
