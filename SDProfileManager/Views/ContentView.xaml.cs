using Microsoft.UI.Xaml.Controls;
using SDProfileManager.Models;
using SDProfileManager.ViewModels;

namespace SDProfileManager.Views;

public sealed partial class ContentView : UserControl
{
    public WorkspaceViewModel ViewModel { get; } = new();

    public ContentView()
    {
        this.InitializeComponent();

        LeftPane.Initialize(ViewModel, PaneSide.Left);
        RightPane.Initialize(ViewModel, PaneSide.Right);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        AutoBalancePanes();
    }

    private void OnUnloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceViewModel.LeftProfile) or nameof(WorkspaceViewModel.RightProfile))
            DispatcherQueue.TryEnqueue(AutoBalancePanes);
    }

    private void AutoBalancePanes()
    {
        var leftWeight = EstimatePaneWeight(ViewModel.LeftProfile);
        var rightWeight = EstimatePaneWeight(ViewModel.RightProfile);

        LeftPaneColumn.Width = new Microsoft.UI.Xaml.GridLength(leftWeight, Microsoft.UI.Xaml.GridUnitType.Star);
        RightPaneColumn.Width = new Microsoft.UI.Xaml.GridLength(rightWeight, Microsoft.UI.Xaml.GridUnitType.Star);
    }

    public void ResetPaneSplit()
    {
        LeftPaneColumn.Width = new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Star);
        RightPaneColumn.Width = new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Star);
    }

    private static double EstimatePaneWeight(ProfileArchive? profile)
    {
        if (profile is null)
            return 1.0;

        var template = profile.Preset;
        var keyDeckWidth = Math.Max(template.Columns, 1) * 84 + Math.Max(template.Columns - 1, 0) * 17;
        var dialDeckWidth = template.HasDialSlots()
            ? Math.Max(template.Dials, 1) * 78 + Math.Max(template.Dials - 1, 0) * 22
            : 0;
        var deckWidth = Math.Max(keyDeckWidth, dialDeckWidth);

        var pageCount = Math.Max(profile.PageOrder.Count, 1);
        var pageStripWidth = 54 + 18 + (pageCount * 37) + 38 + 48;
        var estimated = Math.Max(deckWidth, pageStripWidth);

        var weight = estimated / 700.0;
        return Math.Clamp(weight, 1.0, 2.2);
    }
}
