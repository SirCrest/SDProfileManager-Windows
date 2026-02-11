using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SDProfileManager.Models;
using SDProfileManager.ViewModels;

namespace SDProfileManager.Views;

public sealed partial class PageStripView : UserControl
{
    private WorkspaceViewModel? _viewModel;
    private PaneSide _side;
    private ProfileArchive? _profile;

    private static readonly SolidColorBrush ActiveBg =
        new(Windows.UI.Color.FromArgb(0xDB, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush ActiveFg =
        new(Windows.UI.Color.FromArgb(0xFF, 0x00, 0x00, 0x00));
    private static readonly SolidColorBrush InactiveFg =
        new(Windows.UI.Color.FromArgb(0xA3, 0xFF, 0xFF, 0xFF));

    public PageStripView()
    {
        this.InitializeComponent();
    }

    public void Initialize(WorkspaceViewModel viewModel, PaneSide side, ProfileArchive profile)
    {
        _viewModel = viewModel;
        _side = side;
        _profile = profile;
        RebuildButtons();
    }

    public void Refresh(ProfileArchive profile)
    {
        _profile = profile;
        RebuildButtons();
    }

    private void RebuildButtons()
    {
        if (_viewModel is null || _profile is null) return;

        var canGoBack = _viewModel.CanNavigateFolderBack(_side);
        FolderBackButton.Click -= OnFolderBackClicked;
        FolderBackButton.Click += OnFolderBackClicked;
        FolderBackButton.IsEnabled = canGoBack;
        FolderBackButton.Opacity = canGoBack ? 1.0 : 0.45;
        ToolTipService.SetToolTip(FolderBackButton, canGoBack ? "Back from folder" : "No folder history.");

        PageButtonsPanel.Children.Clear();

        var pageIds = _profile.PageOrder.Count > 0
            ? _profile.PageOrder
            : [_profile.ActivePageId];
        var activePageId = _viewModel.GetViewPageId(_side);

        for (var i = 0; i < pageIds.Count; i++)
        {
            var pageId = pageIds[i];
            var isActive = string.Equals(pageId, activePageId, StringComparison.OrdinalIgnoreCase);
            var canDeletePage = pageIds.Count > 1;
            var pageNumber = i + 1;

            var btn = new Button
            {
                Content = $"{pageNumber}",
                Width = 33,
                Height = 29,
                Padding = new Thickness(0),
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = isActive ? ActiveFg : InactiveFg,
                Background = isActive ? ActiveBg : new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(14),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };

            var capturedPageId = pageId;
            btn.Click += (s, e) => _viewModel.SelectPage(_side, capturedPageId);
            btn.ContextFlyout = BuildPageContextFlyout(capturedPageId, pageNumber, canDeletePage);
            PageButtonsPanel.Children.Add(btn);
        }

        var canAddPage = pageIds.Count < WorkspaceViewModel.MaxSupportedPages;

        // Add page button
        var addBtn = new Button
        {
            Width = 33,
            Height = 29,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(14),
            IsEnabled = canAddPage,
            Opacity = canAddPage ? 1.0 : 0.45,
            Content = new FontIcon
            {
                Glyph = "\uE710",
                FontSize = 13,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF))
            }
        };
        if (canAddPage)
        {
            addBtn.Click += (s, e) => _viewModel.AddPage(_side);
        }
        else
        {
            ToolTipService.SetToolTip(addBtn, $"Maximum {WorkspaceViewModel.MaxSupportedPages} pages.");
        }
        PageButtonsPanel.Children.Add(addBtn);
    }

    private void OnFolderBackClicked(object sender, RoutedEventArgs e)
    {
        _viewModel?.NavigateFolderBack(_side);
    }

    private MenuFlyout BuildPageContextFlyout(string pageId, int pageNumber, bool canDeletePage)
    {
        var flyout = new MenuFlyout();

        var deleteItem = new MenuFlyoutItem
        {
            Text = $"Delete Page {pageNumber}",
            IsEnabled = canDeletePage,
            Icon = new FontIcon
            {
                Glyph = "\uE74D"
            }
        };

        if (canDeletePage)
            deleteItem.Click += (s, e) => _viewModel?.RemovePage(_side, pageId);
        else
            ToolTipService.SetToolTip(deleteItem, "At least one page is required.");

        flyout.Items.Add(deleteItem);
        return flyout;
    }
}
