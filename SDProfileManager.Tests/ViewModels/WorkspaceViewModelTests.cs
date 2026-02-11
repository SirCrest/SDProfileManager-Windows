using System.Text.Json.Nodes;
using SDProfileManager.Models;
using SDProfileManager.Tests.TestData;
using SDProfileManager.ViewModels;

namespace SDProfileManager.Tests.ViewModels;

public class WorkspaceViewModelTests
{
    [Fact]
    public void RemovePage_MissingPage_DoesNotCreateUndoSnapshot()
    {
        var vm = new WorkspaceViewModel();
        var profile = ProfileFactory.Create(pageIds: ["page-a", "page-b"]);
        vm.LeftProfile = profile;
        vm.LeftViewPageId = "page-a";

        vm.RemovePage(PaneSide.Left, "missing-page");

        Assert.False(vm.CanUndo);
        Assert.Equal("Page remove failed.", vm.Status);
        Assert.NotNull(profile.GetPageState("page-a"));
        Assert.NotNull(profile.GetPageState("page-b"));
    }

    [Fact]
    public void DropAction_SharedProfileMove_IgnoresSourceLockAndMovesAcrossPages()
    {
        var vm = new WorkspaceViewModel();
        var profile = ProfileFactory.Create(pageIds: ["left-page", "right-page"]);
        vm.LeftProfile = profile;
        vm.RightProfile = profile;
        vm.LeftViewPageId = "left-page";
        vm.RightViewPageId = "right-page";
        vm.SetSourceLock(true);

        var action = CreateAction();
        profile.SetAction(action.DeepClone(), ControllerKind.Keypad, "0,0", "left-page");
        vm.BeginDrag(PaneSide.Left, ControllerKind.Keypad, "0,0", action, "left-page");

        vm.DropAction(PaneSide.Right, ControllerKind.Keypad, "1,1");

        Assert.Null(profile.GetAction(ControllerKind.Keypad, "0,0", "left-page"));
        Assert.NotNull(profile.GetAction(ControllerKind.Keypad, "1,1", "right-page"));
        Assert.Equal("Moved action within single profile.", vm.Status);
    }

    [Fact]
    public void DropAction_CrossPaneWithSourceLock_CopiesAction()
    {
        var vm = new WorkspaceViewModel();
        var left = ProfileFactory.Create(pageIds: ["left-page"]);
        var right = ProfileFactory.Create(pageIds: ["right-page"]);
        vm.LeftProfile = left;
        vm.RightProfile = right;
        vm.LeftViewPageId = "left-page";
        vm.RightViewPageId = "right-page";
        vm.SetSourceLock(true);

        var action = CreateAction();
        left.SetAction(action.DeepClone(), ControllerKind.Keypad, "0,0", "left-page");
        vm.BeginDrag(PaneSide.Left, ControllerKind.Keypad, "0,0", action, "left-page");

        vm.DropAction(PaneSide.Right, ControllerKind.Keypad, "1,1");

        Assert.NotNull(left.GetAction(ControllerKind.Keypad, "0,0", "left-page"));
        Assert.NotNull(right.GetAction(ControllerKind.Keypad, "1,1", "right-page"));
        Assert.Equal("Copied action to target.", vm.Status);
    }

    private static JsonNode CreateAction()
    {
        return JsonNode.Parse("""
            {
              "Name": "Action",
              "UUID": "com.test.action",
              "State": 0,
              "States": [
                { "Name": "Action", "Image": "" }
              ],
              "Plugin": { "UUID": "com.test.plugin" },
              "Controller": "Keypad"
            }
            """)!;
    }
}
