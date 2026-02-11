using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SDProfileManager.Models;
using SDProfileManager.ViewModels;

namespace SDProfileManager.Views;

public sealed partial class DeckCanvasView : UserControl
{
    private WorkspaceViewModel? _viewModel;
    private PaneSide _side;
    private ProfileArchive? _profile;
    private ScrollViewer? _hostScrollViewer;
    private bool _isApplyingScale;
    private bool _deferredScaleQueued;
    private double _lastAvailWidth = -1;
    private double _lastAvailHeight = -1;

    private KeypadGridView? _keypadGrid;
    private EncoderRowView? _encoderRow;
    private PageStripView? _pageStrip;

    public DeckCanvasView()
    {
        this.InitializeComponent();
        this.SizeChanged += OnSizeChanged;
        this.Loaded += OnLoaded;
        this.Unloaded += OnUnloaded;
    }

    public void Initialize(WorkspaceViewModel viewModel, PaneSide side)
    {
        _viewModel = viewModel;
        _side = side;
    }

    public void SetProfile(ProfileArchive? profile)
    {
        _profile = profile;
        _lastAvailWidth = -1;
        _lastAvailHeight = -1;
        RebuildDeck();
    }

    public void Refresh()
    {
        if (_profile is null || _viewModel is null) return;
        var pageId = _viewModel.GetViewPageId(_side);
        _keypadGrid?.Refresh(_profile, pageId);
        _encoderRow?.Refresh(_profile, pageId);
        _pageStrip?.Refresh(_profile);
        ApplyScale();
        QueueDeferredScale();
    }

    private void RebuildDeck()
    {
        DeckStack.Children.Clear();
        _keypadGrid = null;
        _encoderRow = null;
        _pageStrip = null;

        if (_profile is null || _viewModel is null) return;

        var preset = _profile.Preset;
        var pageId = _viewModel.GetViewPageId(_side);
        var showEncoderSection = preset.HasTouchStrip() || preset.HasDialSlots();

        if (preset.IsTouchStripAboveKeys() && showEncoderSection)
            AddEncoderSection();

        // Key grid
        _keypadGrid = new KeypadGridView();
        _keypadGrid.Initialize(_viewModel, _side, _profile, pageId);
        DeckStack.Children.Add(_keypadGrid);

        if (!preset.IsTouchStripAboveKeys() && showEncoderSection)
            AddEncoderSection();

        // Page strip
        _pageStrip = new PageStripView();
        _pageStrip.Initialize(_viewModel, _side, _profile);
        DeckStack.Children.Add(_pageStrip);

        ApplyScale();
        QueueDeferredScale();
    }

    private void AddEncoderSection()
    {
        if (_viewModel is null || _profile is null) return;
        var pageId = _viewModel.GetViewPageId(_side);
        _encoderRow = new EncoderRowView();
        _encoderRow.Initialize(_viewModel, _side, _profile, pageId);
        DeckStack.Children.Add(_encoderRow);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyScale();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachHostScrollViewer();
        ApplyScale();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachHostScrollViewer();
    }

    private void OnHostScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyScale();
    }

    private void AttachHostScrollViewer()
    {
        var host = FindAncestorScrollViewer(this);
        if (ReferenceEquals(host, _hostScrollViewer))
            return;

        DetachHostScrollViewer();
        _hostScrollViewer = host;
        if (_hostScrollViewer is not null)
            _hostScrollViewer.SizeChanged += OnHostScrollViewerSizeChanged;
    }

    private void DetachHostScrollViewer()
    {
        if (_hostScrollViewer is not null)
        {
            _hostScrollViewer.SizeChanged -= OnHostScrollViewerSizeChanged;
            _hostScrollViewer = null;
        }
    }

    private static ScrollViewer? FindAncestorScrollViewer(DependencyObject start)
    {
        var current = VisualTreeHelper.GetParent(start);
        while (current is not null)
        {
            if (current is ScrollViewer scrollViewer)
                return scrollViewer;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void ApplyScale()
    {
        if (_profile is null || _isApplyingScale)
            return;

        AttachHostScrollViewer();
        double viewportWidth;
        double viewportHeight;
        if (_hostScrollViewer is not null)
        {
            viewportWidth = _hostScrollViewer.ViewportWidth;
            viewportHeight = _hostScrollViewer.ViewportHeight;

            // Wait for a real viewport after page/pane layout settles.
            if (viewportWidth <= 1 || viewportHeight <= 1)
            {
                QueueDeferredScale();
                return;
            }
        }
        else
        {
            // Fallback path (should be rare) when no ancestor ScrollViewer is available.
            viewportWidth = ActualWidth;
            viewportHeight = ActualHeight;
            if (viewportWidth <= 1 || viewportHeight <= 1)
                return;
        }

        var availW = Math.Max(viewportWidth - 26, 220);
        var availH = Math.Max(viewportHeight - 72, 220);
        if (Math.Abs(availW - _lastAvailWidth) < 0.5 && Math.Abs(availH - _lastAvailHeight) < 0.5)
            return;

        _isApplyingScale = true;
        try
        {
            var metrics = ComputeMetrics(_profile.Preset, availW, availH);

            _keypadGrid?.ApplyMetrics(metrics, _profile.Preset);
            _encoderRow?.ApplyMetrics(metrics, _profile.Preset);

            DeckStack.Spacing = Math.Max(8, 12 * metrics.Scale);
            _lastAvailWidth = availW;
            _lastAvailHeight = availH;
        }
        finally
        {
            _isApplyingScale = false;
        }
    }

    private void QueueDeferredScale()
    {
        if (_deferredScaleQueued)
            return;

        var dispatcher = DispatcherQueue;
        if (dispatcher is null)
            return;

        _deferredScaleQueued = true;
        dispatcher.TryEnqueue(() =>
        {
            _deferredScaleQueued = false;
            ApplyScale();
        });
    }

    internal static DeckMetrics ComputeMetrics(ProfileTemplate preset, double availW, double availH)
    {
        var columns = Math.Max(preset.Columns, 1);
        var rows = Math.Max(preset.Rows, 1);
        var hasTouchStrip = preset.HasTouchStrip();
        var hasDialSlots = preset.HasDialSlots();
        var stripRows = Math.Max(preset.GetTouchStripRows(), hasTouchStrip ? 1 : 0);

        const double baseKeyEdge = 84;
        const double baseKeySpacing = 17;
        const double baseDialDiameter = 78;
        const double baseDialSpacing = 22;
        const double baseStripSingleRowHeight = 84;
        const double baseStripMultiRowHeight = 64;
        const double baseStripRowGap = 8;
        const double baseGapAfterKeys = 14;
        const double baseGapAfterStrip = 18;
        const double pageStripReserve = 48;

        var baseStageWidth = columns * baseKeyEdge + (columns - 1) * baseKeySpacing;
        var baseGridHeight = rows * baseKeyEdge + (rows - 1) * baseKeySpacing;
        var baseDialRowWidth = hasDialSlots
            ? Math.Max(preset.Dials, 1) * baseDialDiameter + Math.Max(preset.Dials - 1, 0) * baseDialSpacing
            : 0;
        var baseStripRowHeight = stripRows <= 1 ? baseStripSingleRowHeight : baseStripMultiRowHeight;

        var deckWidth = Math.Max(baseStageWidth, baseDialRowWidth);
        var baseStripTotalHeight = hasTouchStrip
            ? (stripRows * baseStripRowHeight) + (Math.Max(stripRows - 1, 0) * baseStripRowGap)
            : 0;
        var encoderSectionHeight = 0.0;
        if (hasTouchStrip)
            encoderSectionHeight += baseStripTotalHeight;
        if (hasDialSlots)
        {
            if (hasTouchStrip)
                encoderSectionHeight += baseGapAfterStrip;
            encoderSectionHeight += baseDialDiameter;
        }

        var deckHeight = baseGridHeight + (encoderSectionHeight > 0 ? (baseGapAfterKeys + encoderSectionHeight) : 0);

        var usableWidth = Math.Max(availW, 220);
        var usableHeight = Math.Max(availH - pageStripReserve - 24, 220);
        var scale = Math.Min(usableWidth / deckWidth, usableHeight / deckHeight);
        // Keep a very low floor; slot-specific minimum pixel sizes below
        // are the practical lower bound and prevent hard clipping in short panes.
        const double minScale = 0.10;
        var resolvedScale = Math.Clamp(scale, minScale, 1.08);

        var keyEdge = Math.Max(30, baseKeyEdge * resolvedScale);
        var keySpacing = Math.Max(5, baseKeySpacing * resolvedScale);
        var dialDiameter = Math.Max(28, baseDialDiameter * resolvedScale);
        var dialSpacing = Math.Max(7, baseDialSpacing * resolvedScale);
        var stripRowGap = Math.Max(4, baseStripRowGap * resolvedScale);
        var stripHeight = hasTouchStrip
            ? Math.Max(26, (stripRows * baseStripRowHeight * resolvedScale) + (Math.Max(stripRows - 1, 0) * stripRowGap))
            : 0;
        var keyCorner = Math.Max(9, keyEdge * 0.2);

        var stageWidth = Math.Max(
            columns * keyEdge + (columns - 1) * keySpacing,
            hasDialSlots ? (Math.Max(preset.Dials, 1) * dialDiameter + Math.Max(preset.Dials - 1, 0) * dialSpacing) : 0);

        return new DeckMetrics
        {
            KeyEdge = keyEdge,
            KeySpacing = keySpacing,
            KeyCorner = keyCorner,
            DialDiameter = dialDiameter,
            DialSpacing = dialSpacing,
            StripHeight = stripHeight,
            Scale = resolvedScale,
            StageWidth = stageWidth
        };
    }
}

public class DeckMetrics
{
    public double KeyEdge { get; set; }
    public double KeySpacing { get; set; }
    public double KeyCorner { get; set; }
    public double DialDiameter { get; set; }
    public double DialSpacing { get; set; }
    public double StripHeight { get; set; }
    public double Scale { get; set; }
    public double StageWidth { get; set; }
}
