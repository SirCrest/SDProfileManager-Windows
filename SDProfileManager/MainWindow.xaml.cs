using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI;
using SDProfileManager.Models;
using SDProfileManager.ViewModels;
using Windows.Foundation;
using Windows.System;
using WinRT.Interop;

namespace SDProfileManager;

public sealed partial class MainWindow : Window
{
    private readonly WorkspaceViewModel _viewModel;
    private readonly AppWindow _appWindow;
    private Windows.Graphics.SizeInt32? _pendingAutoResize;

    public MainWindow()
    {
        this.InitializeComponent();

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Changed += OnAppWindowChanged;
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
            _appWindow.SetIcon(iconPath);

        _viewModel = RootContentView.ViewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        var startupSize = ComputeTargetWindowSize(windowId, _viewModel.LeftProfile, _viewModel.RightProfile);
        ResizeWindow(startupSize);
        Title = "SD Profile Manager";

        RootContentView.KeyboardAccelerators.Add(MakeAccelerator(VirtualKey.Z, VirtualKeyModifiers.Control, OnUndo));
        RootContentView.KeyboardAccelerators.Add(MakeAccelerator(VirtualKey.Y, VirtualKeyModifiers.Control, OnRedo));
        RootContentView.KeyboardAccelerators.Add(MakeAccelerator(VirtualKey.Z, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, OnRedo));
        RootContentView.KeyboardAccelerators.Add(MakeAccelerator(VirtualKey.O, VirtualKeyModifiers.Control, OnOpenSource));
        RootContentView.KeyboardAccelerators.Add(MakeAccelerator(VirtualKey.S, VirtualKeyModifiers.Control, OnSaveTarget));
    }

    private static KeyboardAccelerator MakeAccelerator(VirtualKey key, VirtualKeyModifiers modifiers, TypedEventHandler<KeyboardAccelerator, KeyboardAcceleratorInvokedEventArgs> handler)
    {
        var accel = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accel.Invoked += handler;
        return accel;
    }

    private void OnUndo(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        RootContentView.ViewModel.UndoCommand.Execute(null);
        args.Handled = true;
    }

    private void OnRedo(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        RootContentView.ViewModel.RedoCommand.Execute(null);
        args.Handled = true;
    }

    private void OnOpenSource(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        RootContentView.ViewModel.OpenProfileCommand.Execute(PaneSide.Left);
        args.Handled = true;
    }

    private void OnSaveTarget(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        RootContentView.ViewModel.SaveProfileCommand.Execute(PaneSide.Right);
        args.Handled = true;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(WorkspaceViewModel.LeftProfile) or nameof(WorkspaceViewModel.RightProfile)))
            return;

        AutoGrowWindowForProfiles();
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidSizeChange)
            return;

        var pending = _pendingAutoResize;
        if (pending is not null)
        {
            _pendingAutoResize = null;
            return;
        }
    }

    private void AutoGrowWindowForProfiles()
    {
        var target = ComputeTargetWindowSize(_appWindow.Id, _viewModel.LeftProfile, _viewModel.RightProfile);
        var current = _appWindow.Size;

        var nextWidth = Math.Max(current.Width, target.Width);
        var nextHeight = Math.Max(current.Height, target.Height);
        if (nextWidth == current.Width && nextHeight == current.Height)
            return;

        ResizeWindow(new Windows.Graphics.SizeInt32(nextWidth, nextHeight));
    }

    private static int EstimatePaneWidth(ProfileArchive? profile)
    {
        if (profile is null)
            return 860;

        var template = profile.Preset;
        const double targetScale = 0.9;

        var keyDeckWidth = Math.Max(template.Columns, 1) * 84 + Math.Max(template.Columns - 1, 0) * 17;
        var dialDeckWidth = template.HasDialSlots()
            ? Math.Max(template.Dials, 1) * 78 + Math.Max(template.Dials - 1, 0) * 22
            : 0;
        var deckWidth = Math.Max(keyDeckWidth, dialDeckWidth) * targetScale + 90;

        var pageCount = Math.Max(profile.PageOrder.Count, 1);
        var pageStripWidth = 54 + 18 + (pageCount * 37) + 38 + 48;

        var controlsWidth = template.Columns >= 9 || template.Dials >= 6 ? 860 : 760;

        var estimated = Math.Max(deckWidth, Math.Max(pageStripWidth, controlsWidth));
        return Math.Max(620, (int)Math.Ceiling(estimated));
    }

    private static int EstimateWindowHeight(ProfileArchive? left, ProfileArchive? right)
    {
        var template = PickWidestTemplate(left, right);
        if (template is null)
            return 1080;

        const double targetScale = 0.9;
        var gridHeight = Math.Max(template.Rows, 1) * 84 + Math.Max(template.Rows - 1, 0) * 17;
        var encoderSectionHeight = 0.0;
        var stripRows = Math.Max(template.GetTouchStripRows(), template.HasTouchStrip() ? 1 : 0);
        var stripRowHeight = stripRows <= 1 ? 84 : 64;
        if (template.HasTouchStrip())
            encoderSectionHeight += (stripRowHeight * stripRows) + (Math.Max(stripRows - 1, 0) * 8);
        if (template.HasDialSlots())
        {
            if (template.HasTouchStrip())
                encoderSectionHeight += 18;
            encoderSectionHeight += 78;
        }

        var dialSectionHeight = encoderSectionHeight > 0 ? (14 + encoderSectionHeight) : 0;
        var deckHeight = ((gridHeight + dialSectionHeight) * targetScale) + 72; // page strip + stage padding

        var maxPages = Math.Max(left?.PageOrder.Count ?? 1, right?.PageOrder.Count ?? 1);
        var pageStripExtra = Math.Max(0, maxPages - 8) * 8;

        var paneHeight = deckHeight + 360 + pageStripExtra;  // pane header + controls + preflight + paddings
        var windowHeight = paneHeight + 260; // app header + status bar + root margins
        return Math.Max(1040, (int)Math.Ceiling(windowHeight));
    }

    private static ProfileTemplate? PickWidestTemplate(ProfileArchive? left, ProfileArchive? right)
    {
        if (left is null) return right?.Preset;
        if (right is null) return left.Preset;

        var leftScore = left.Preset.Columns + (left.Preset.Dials * 0.8);
        var rightScore = right.Preset.Columns + (right.Preset.Dials * 0.8);
        return leftScore >= rightScore ? left.Preset : right.Preset;
    }

    private static Windows.Graphics.SizeInt32 ComputeTargetWindowSize(Microsoft.UI.WindowId windowId, ProfileArchive? left, ProfileArchive? right)
    {
        var workArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary).WorkArea;

        var leftPane = EstimatePaneWidth(left);
        var rightPane = EstimatePaneWidth(right);
        var baseDesiredWidth = leftPane + rightPane + 10 + 88; // splitter + outer paddings/margins
        var baseDesiredHeight = EstimateWindowHeight(left, right);

        // Bias startup/autogrow aggressively so keys are large enough to keep icon/text legible.
        var desiredWidth = (int)Math.Ceiling(baseDesiredWidth * 1.6);
        var desiredHeight = (int)Math.Ceiling(baseDesiredHeight * 1.5);

        var widthCap = Math.Min(2400, workArea.Width);
        var heightCap = Math.Min(1600, workArea.Height);

        var widthFloor = Math.Min(2400, widthCap);
        var heightFloor = Math.Min(1600, heightCap);

        var width = Math.Clamp(Math.Max(desiredWidth, widthFloor), 1500, widthCap);
        var height = Math.Clamp(Math.Max(desiredHeight, heightFloor), 960, heightCap);
        return new Windows.Graphics.SizeInt32(width, height);
    }

    private void ResizeWindow(Windows.Graphics.SizeInt32 size)
    {
        _pendingAutoResize = size;
        _appWindow.Resize(size);
    }

    public void RecoverFromLayoutCycle()
    {
        RootContentView.ResetPaneSplit();
    }
}
