using System.Text.Json.Nodes;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SDProfileManager.Helpers;
using SDProfileManager.Models;
using SDProfileManager.Services;
using SDProfileManager.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace SDProfileManager.Views;

public sealed partial class ActionSlotControl : UserControl
{
    private WorkspaceViewModel? _viewModel;
    private PaneSide _side;
    private ProfileArchive? _profile;
    private ControllerKind _controller;
    private string _coordinate = "";
    private string _pageId = "";
    private bool _isDropTarget;
    private DateTimeOffset _lastFolderBackNavigateAt = DateTimeOffset.MinValue;

    private static readonly SolidColorBrush DefaultBorderBrush =
        new(Windows.UI.Color.FromArgb(0x29, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush DropHighlightBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0x6E, 0xBD, 0xFF));
    private static readonly SolidColorBrush DefaultBgBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0x1C, 0x1C, 0x1E));
    private static readonly SolidColorBrush DropBgBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0x1C, 0x24, 0x30));
    private static readonly SolidColorBrush SelectedBorderBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0x9B, 0xC3, 0xFF));
    private static readonly SolidColorBrush SelectedBgBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0x21, 0x26, 0x31));

    public ActionSlotControl()
    {
        this.InitializeComponent();
        IsTabStop = true;
        PointerPressed += OnPointerPressed;
        Tapped += OnTapped;
        DoubleTapped += OnDoubleTapped;
        GotFocus += OnFocusChanged;
        LostFocus += OnFocusChanged;
        KeyDown += OnKeyDown;
        UpdateSlotChrome();
    }

    public void Initialize(WorkspaceViewModel viewModel, PaneSide side, ProfileArchive profile, ControllerKind controller, string coordinate, string pageId)
    {
        _viewModel = viewModel;
        _side = side;
        _profile = profile;
        _controller = controller;
        _coordinate = coordinate;
        _pageId = pageId;

        CanDrag = false; // Will be set in Refresh
        this.DragStarting += OnDragStarting;

        Refresh(profile, pageId);
    }

    public void Refresh(ProfileArchive profile, string pageId)
    {
        _profile = profile;
        _pageId = pageId;
        var action = profile.GetAction(_controller, _coordinate, _pageId);
        var isReservedFolderBackSlot = IsReservedFolderBackSlot();
        var hasAction = action is not null;

        CanDrag = hasAction && !isReservedFolderBackSlot;

        try
        {
            if (hasAction)
            {
                ShowFilledSlot(action!, _pageId);
            }
            else
            {
                ShowEmptySlot();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"Action slot render failed side={_side} coord={_coordinate} controller={_controller} error={ex}");
            ShowEmptySlot();
        }

        FolderBackOverlay.Visibility = isReservedFolderBackSlot ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(this, isReservedFolderBackSlot ? "Back from folder" : null);
        UpdateSlotChrome();
    }

    public void SetCornerRadius(double radius)
    {
        SlotBorder.CornerRadius = new CornerRadius(radius);
        FallbackBorder.CornerRadius = new CornerRadius(Math.Max(7, radius * 0.75));
    }

    private void ShowEmptySlot()
    {
        ActionImage.Visibility = Visibility.Collapsed;
        FallbackBorder.Visibility = Visibility.Collapsed;
        TitleOverlay.Visibility = Visibility.Collapsed;
    }

    private void ShowFilledSlot(JsonNode action, string pageId)
    {
        if (_profile is null || _viewModel is null) return;
        var slotSize = ResolveSlotSize(84);

        var presentation = _profile.GetActionPresentation(action);

        // Try to load image
        var imageRef = presentation.ImageReference;
        if (imageRef is not null)
        {
            var bitmap = _viewModel.ImageCache.GetImage(_profile, imageRef, pageId);
            if (bitmap is not null)
            {
                ActionImage.Source = bitmap;
                ActionImage.Visibility = Visibility.Visible;
                ActionImage.Margin = new Thickness(Math.Max(6, slotSize * 0.1));
                FallbackBorder.Visibility = Visibility.Collapsed;
            }
            else
            {
                ShowFallback(presentation.PluginName);
            }
        }
        else
        {
            ShowFallback(presentation.PluginName);
        }

        // Title overlay
        TitleText.Text = presentation.Title;
        TitleText.FontSize = Math.Max(8, slotSize * 0.11);
        TitleOverlay.Visibility = Visibility.Visible;
    }

    private void ShowFallback(string pluginName)
    {
        var slotSize = ResolveSlotSize(84);
        ActionImage.Visibility = Visibility.Collapsed;
        FallbackBorder.Visibility = Visibility.Visible;
        FallbackText.Text = InitialsHelper.GetInitials(pluginName);
        FallbackText.FontSize = Math.Max(10, slotSize * 0.18);
    }

    private double ResolveSlotSize(double fallback)
    {
        var fromWidth = (!double.IsNaN(Width) && Width > 0) ? Width : fallback;
        var fromActual = (!double.IsNaN(ActualWidth) && ActualWidth > 0) ? ActualWidth : fromWidth;
        return fromActual;
    }

    // --- Drag & Drop ---

    private void OnDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (IsReservedFolderBackSlot())
        {
            args.Cancel = true;
            return;
        }

        if (_viewModel is null || _profile is null) return;

        var action = _profile.GetAction(_controller, _coordinate, _pageId);
        if (action is null) return;

        _viewModel.BeginDrag(_side, _controller, _coordinate, action, _pageId);
        args.Data.SetText("profile-action");
        args.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (IsReservedFolderBackSlot())
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        if (_viewModel?.DragContext is null) return;
        if (_viewModel.DragContext.Controller != _controller)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Move;
        if (!_isDropTarget)
        {
            _isDropTarget = true;
            UpdateSlotChrome();
        }
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        if (_isDropTarget)
        {
            _isDropTarget = false;
            UpdateSlotChrome();
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        _isDropTarget = false;
        UpdateSlotChrome();

        if (IsReservedFolderBackSlot())
            return;

        if (_viewModel?.DragContext is null) return;
        _viewModel.DropAction(_side, _controller, _coordinate);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _ = Focus(FocusState.Programmatic);
        UpdateSlotChrome();
    }

    private void OnTapped(object sender, TappedRoutedEventArgs e)
    {
        if (!IsReservedFolderBackSlot() || _viewModel is null)
            return;

        if (!CanTriggerFolderBackNavigation())
        {
            e.Handled = true;
            return;
        }

        _viewModel.NavigateFolderBack(_side);
        e.Handled = true;
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        _ = Focus(FocusState.Programmatic);
        if (IsReservedFolderBackSlot() && _viewModel is not null)
        {
            if (CanTriggerFolderBackNavigation())
                _viewModel.NavigateFolderBack(_side);
            e.Handled = true;
            return;
        }

        _viewModel?.OpenFolderAction(_side, _controller, _coordinate);
    }

    private void OnFocusChanged(object sender, RoutedEventArgs e)
    {
        UpdateSlotChrome();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (IsReservedFolderBackSlot())
        {
            if (e.Key is VirtualKey.Enter or VirtualKey.Space or VirtualKey.Back or VirtualKey.Delete)
            {
                _viewModel?.NavigateFolderBack(_side);
                e.Handled = true;
            }
            return;
        }

        if (e.Key is not (VirtualKey.Delete or VirtualKey.Back))
            return;

        _viewModel?.RemoveAction(_side, _controller, _coordinate);
        e.Handled = true;
    }

    private void UpdateSlotChrome()
    {
        if (_isDropTarget)
        {
            SlotBorder.BorderBrush = DropHighlightBrush;
            SlotBorder.Background = DropBgBrush;
            return;
        }

        if (FocusState != FocusState.Unfocused)
        {
            SlotBorder.BorderBrush = SelectedBorderBrush;
            SlotBorder.Background = SelectedBgBrush;
            return;
        }

        SlotBorder.BorderBrush = DefaultBorderBrush;
        SlotBorder.Background = DefaultBgBrush;
    }

    private bool IsReservedFolderBackSlot() =>
        _controller == ControllerKind.Keypad
        && string.Equals(_coordinate, "0,0", StringComparison.OrdinalIgnoreCase)
        && _viewModel?.CanNavigateFolderBack(_side) == true;

    private bool CanTriggerFolderBackNavigation()
    {
        var now = DateTimeOffset.UtcNow;
        if ((now - _lastFolderBackNavigateAt).TotalMilliseconds < 220)
            return false;

        _lastFolderBackNavigateAt = now;
        return true;
    }
}
