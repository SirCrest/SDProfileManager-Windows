using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SDProfileManager.Models;
using SDProfileManager.ViewModels;

namespace SDProfileManager.Views;

public sealed partial class KeypadGridView : UserControl
{
    private WorkspaceViewModel? _viewModel;
    private PaneSide _side;
    private ProfileArchive? _profile;
    private string _pageId = "";
    private readonly List<ActionSlotControl> _slots = [];

    public KeypadGridView()
    {
        this.InitializeComponent();
    }

    public void Initialize(WorkspaceViewModel viewModel, PaneSide side, ProfileArchive profile, string pageId)
    {
        _viewModel = viewModel;
        _side = side;
        _profile = profile;
        _pageId = pageId;
        RebuildGrid(profile.Preset);
    }

    public void Refresh(ProfileArchive profile, string pageId)
    {
        _profile = profile;
        _pageId = pageId;
        foreach (var slot in _slots)
            slot.Refresh(profile, _pageId);
    }

    public void ApplyMetrics(DeckMetrics metrics, ProfileTemplate preset)
    {
        KeyGrid.Width = metrics.StageWidth;
        KeyGrid.ColumnSpacing = metrics.KeySpacing;
        KeyGrid.RowSpacing = metrics.KeySpacing;

        foreach (var slot in _slots)
        {
            slot.Width = metrics.KeyEdge;
            slot.Height = metrics.KeyEdge;
            slot.SetCornerRadius(metrics.KeyCorner);
        }
    }

    private void RebuildGrid(ProfileTemplate preset)
    {
        if (_viewModel is null || _profile is null) return;
        KeyGrid.Children.Clear();
        KeyGrid.ColumnDefinitions.Clear();
        KeyGrid.RowDefinitions.Clear();
        _slots.Clear();

        var cols = Math.Max(preset.Columns, 1);
        var rows = Math.Max(preset.Rows, 1);

        for (var c = 0; c < cols; c++)
            KeyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var r = 0; r < rows; r++)
            KeyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < cols; x++)
            {
                var coordinate = $"{x},{y}";
                var slot = new ActionSlotControl();
                slot.Initialize(_viewModel, _side, _profile, ControllerKind.Keypad, coordinate, _pageId);
                Grid.SetColumn(slot, x);
                Grid.SetRow(slot, y);
                KeyGrid.Children.Add(slot);
                _slots.Add(slot);
            }
        }
    }
}
