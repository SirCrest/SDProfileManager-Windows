using System.Text.Json.Nodes;
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

public sealed partial class DialSlotControl : UserControl
{
    private WorkspaceViewModel? _viewModel;
    private PaneSide _side;
    private ProfileArchive? _profile;
    private string _coordinate = "";
    private string _pageId = "";
    private int _dialColumn;
    private bool _isDropTarget;

    private static readonly SolidColorBrush DefaultBorderBrush =
        new(Windows.UI.Color.FromArgb(0x2B, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush DropHighlightBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0x6E, 0xBD, 0xFF));
    private static readonly SolidColorBrush SelectedBorderBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0x9B, 0xC3, 0xFF));

    public DialSlotControl()
    {
        this.InitializeComponent();
        IsTabStop = true;
        PointerPressed += OnPointerPressed;
        GotFocus += OnFocusChanged;
        LostFocus += OnFocusChanged;
        KeyDown += OnKeyDown;
        UpdateDialChrome();
    }

    public void Initialize(WorkspaceViewModel viewModel, PaneSide side, ProfileArchive profile, string coordinate, string pageId)
    {
        _viewModel = viewModel;
        _side = side;
        _profile = profile;
        _coordinate = coordinate;
        _pageId = pageId;
        ParseCoordinate(coordinate, out _dialColumn);
        DialBadgeText.Text = $"D{_dialColumn + 1}";

        CanDrag = false;
        this.DragStarting += OnDragStarting;

        Refresh(profile, pageId);
    }

    public void Refresh(ProfileArchive profile, string pageId)
    {
        _profile = profile;
        _pageId = pageId;
        var action = profile.GetAction(ControllerKind.Encoder, _coordinate, _pageId);
        var hasAction = action is not null;

        CanDrag = hasAction;

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
            AppLog.Error($"Dial slot render failed side={_side} coord={_coordinate} error={ex}");
            ShowEmptySlot();
        }

        UpdateDialChrome();
    }

    private void ShowEmptySlot()
    {
        ActionImageBrush.ImageSource = null;
        ActionImageClip.Visibility = Visibility.Collapsed;
        FallbackText.Visibility = Visibility.Collapsed;
        DialBadge.Visibility = Visibility.Collapsed;
        if (_profile is not null)
            ToolTipService.SetToolTip(this, BuildDialToolTip(_profile.Preset, null));
    }

    private void ShowFilledSlot(JsonNode action, string pageId)
    {
        if (_profile is null || _viewModel is null) return;
        var slotSize = ResolveSlotSize(78);

        var presentation = _profile.GetActionPresentation(action);
        var imageRef = presentation.ImageReference;
        DialBadge.Visibility = Visibility.Visible;

        if (imageRef is not null)
        {
            var bitmap = _viewModel.ImageCache.GetImage(_profile, imageRef, pageId);
            if (bitmap is not null)
            {
                ActionImageBrush.ImageSource = bitmap;
                ActionImageClip.Visibility = Visibility.Visible;
                ActionImageClip.Margin = new Thickness(Math.Max(8, slotSize * 0.18));
                FallbackText.Visibility = Visibility.Collapsed;
            }
            else
            {
                ShowFallbackInitials(presentation, slotSize);
            }
        }
        else
        {
            ShowFallbackInitials(presentation, slotSize);
        }

        ToolTipService.SetToolTip(this, BuildDialToolTip(_profile.Preset, presentation));
    }

    private void ShowFallbackInitials(ActionPresentation presentation, double slotSize)
    {
        ActionImageClip.Visibility = Visibility.Collapsed;
        FallbackText.Visibility = Visibility.Visible;
        FallbackText.Text = InitialsHelper.GetInitials(presentation.ActionName);
        FallbackText.FontSize = Math.Max(10, slotSize * 0.2);
    }

    private double ResolveSlotSize(double fallback)
    {
        var fromWidth = (!double.IsNaN(Width) && Width > 0) ? Width : fallback;
        var fromActual = (!double.IsNaN(ActualWidth) && ActualWidth > 0) ? ActualWidth : fromWidth;
        return fromActual;
    }

    private string BuildDialToolTip(ProfileTemplate preset, ActionPresentation? presentation)
    {
        var dialName = $"Dial {_dialColumn + 1}";
        var stripHint = BuildStripHint(preset);
        if (presentation is null)
            return $"{dialName}\n{stripHint}\nDrop an encoder action here.\nCoordinate: {_coordinate}";

        return $"{dialName}\nAction: {presentation.ActionName}\nPlugin: {presentation.PluginName}\n{stripHint}\nCoordinate: {_coordinate}";
    }

    private string BuildStripHint(ProfileTemplate preset)
    {
        if (!preset.HasTouchStrip())
            return "No touch strip on this device.";

        if (preset.GetTouchStripRows() <= 1)
            return $"Touch strip segment: Dial {_dialColumn + 1}";

        if (string.Equals(preset.Id, "g100sd", StringComparison.OrdinalIgnoreCase))
            return $"Touch strip segments: Dial {_dialColumn + 1} Top and Dial {_dialColumn + 1} Bottom";

        return $"Touch strip segments: Dial {_dialColumn + 1} rows 1-{preset.GetTouchStripRows()}";
    }

    private static void ParseCoordinate(string coordinate, out int x)
    {
        x = 0;
        var parts = coordinate.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return;

        _ = int.TryParse(parts[0], out x);
    }

    // --- Drag & Drop ---

    private void OnDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (_viewModel is null || _profile is null) return;

        var action = _profile.GetAction(ControllerKind.Encoder, _coordinate, _pageId);
        if (action is null) return;

        _viewModel.BeginDrag(_side, ControllerKind.Encoder, _coordinate, action, _pageId);
        args.Data.SetText("profile-action");
        args.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (_viewModel?.DragContext is null) return;
        if (_viewModel.DragContext.Controller != ControllerKind.Encoder)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Move;
        if (!_isDropTarget)
        {
            _isDropTarget = true;
            UpdateDialChrome();
        }
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        if (_isDropTarget)
        {
            _isDropTarget = false;
            UpdateDialChrome();
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        _isDropTarget = false;
        UpdateDialChrome();

        if (_viewModel?.DragContext is null) return;
        _viewModel.DropAction(_side, ControllerKind.Encoder, _coordinate);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _ = Focus(FocusState.Programmatic);
        UpdateDialChrome();
    }

    private void OnFocusChanged(object sender, RoutedEventArgs e)
    {
        UpdateDialChrome();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not (VirtualKey.Delete or VirtualKey.Back))
            return;

        _viewModel?.RemoveAction(_side, ControllerKind.Encoder, _coordinate);
        e.Handled = true;
    }

    private void UpdateDialChrome()
    {
        if (_isDropTarget)
        {
            DialBg.Stroke = DropHighlightBrush;
            return;
        }

        DialBg.Stroke = FocusState == FocusState.Unfocused
            ? DefaultBorderBrush
            : SelectedBorderBrush;
    }
}
