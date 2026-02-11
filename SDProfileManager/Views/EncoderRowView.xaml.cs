using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SDProfileManager.Models;
using SDProfileManager.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace SDProfileManager.Views;

public sealed partial class EncoderRowView : UserControl
{
    private sealed class StripSegmentSlot
    {
        public Border Border { get; init; } = null!;
        public TextBlock Mapping { get; init; } = null!;
        public Border AvailabilityBadge { get; init; } = null!;
        public TextBlock AvailabilityBadgeText { get; init; } = null!;
        public Viewbox LayoutViewbox { get; init; } = null!;
        public Canvas LayoutCanvas { get; init; } = null!;
        public Image FallbackIcon { get; init; } = null!;
        public TextBlock Label { get; init; } = null!;
        public TextBlock SecondaryLabel { get; init; } = null!;
        public string Coordinate { get; init; } = "";
        public bool IsDropTarget { get; set; }
        public double BaseLabelFontSize { get; set; }
    }

    private WorkspaceViewModel? _viewModel;
    private PaneSide _side;
    private ProfileArchive? _profile;
    private string _pageId = "";
    private string? _selectedStripCoordinate;
    private readonly List<DialSlotControl> _slots = [];
    private readonly List<StripSegmentSlot> _stripSegments = [];
    private readonly Dictionary<string, ImageSource?> _imageSourceCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly SolidColorBrush SegmentBorderBrush =
        new(Windows.UI.Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush SegmentBackgroundBrush =
        new(Windows.UI.Color.FromArgb(0x40, 0x00, 0x00, 0x00));
    private static readonly SolidColorBrush SegmentDropHighlightBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0x6E, 0xBD, 0xFF));
    private static readonly SolidColorBrush SegmentDropBackgroundBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0x1C, 0x24, 0x30));
    private static readonly SolidColorBrush SegmentSelectedBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0x9B, 0xC3, 0xFF));
    private static readonly SolidColorBrush SegmentSelectedBackgroundBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0x20, 0x25, 0x31));
    private static readonly SolidColorBrush SegmentLayerBackgroundBrush =
        new(Windows.UI.Color.FromArgb(0x38, 0x00, 0x00, 0x00));
    private static readonly SolidColorBrush SegmentWarningBadgeBrush =
        new(Windows.UI.Color.FromArgb(0xD0, 0x45, 0x2E, 0x10));
    private static readonly SolidColorBrush SegmentWarningBadgeBorderBrush =
        new(Windows.UI.Color.FromArgb(0xA8, 0xFF, 0xBE, 0x69));

    public EncoderRowView()
    {
        this.InitializeComponent();
        IsTabStop = true;
        GotFocus += OnHostGotFocus;
        LostFocus += OnHostLostFocus;
        KeyDown += OnKeyDown;
    }

    public void Initialize(WorkspaceViewModel viewModel, PaneSide side, ProfileArchive profile, string pageId)
    {
        _viewModel = viewModel;
        _side = side;
        _profile = profile;
        _pageId = pageId;
        RebuildDials(profile.Preset);
    }

    public void Refresh(ProfileArchive profile, string pageId)
    {
        _profile = profile;
        _pageId = pageId;
        foreach (var slot in _slots)
            slot.Refresh(profile, _pageId);
        RefreshStripSegments(profile);
    }

    public void ApplyMetrics(DeckMetrics metrics, ProfileTemplate preset)
    {
        var showStrip = preset.HasTouchStrip();
        var showDials = preset.HasDialSlots();
        var stripRows = Math.Max(1, preset.GetTouchStripRows());
        var stripCols = Math.Max(1, preset.GetTouchStripColumns());

        StripBar.Visibility = showStrip ? Visibility.Visible : Visibility.Collapsed;
        DialRow.Visibility = showDials ? Visibility.Visible : Visibility.Collapsed;

        if (showStrip)
        {
            StripBar.Width = metrics.StageWidth;
            StripBar.Height = metrics.StripHeight;
            StripBar.CornerRadius = new CornerRadius(Math.Max(6, metrics.StripHeight * 0.14));

            StripGrid.Padding = new Thickness(
                Math.Max(4, 8 * metrics.Scale),
                Math.Max(3, 6 * metrics.Scale),
                Math.Max(4, 8 * metrics.Scale),
                Math.Max(3, 6 * metrics.Scale));
            StripGrid.ColumnSpacing = Math.Max(4, 8 * metrics.Scale);
            StripGrid.RowSpacing = stripRows > 1 ? Math.Max(3, 6 * metrics.Scale) : 0;

            var iconSize = stripRows > 1
                ? Math.Max(11, metrics.StripHeight * 0.17)
                : Math.Max(13, metrics.StripHeight * 0.22);
            var labelFontSize = stripRows > 1
                ? Math.Max(8, metrics.StripHeight * 0.11)
                : Math.Max(9, metrics.StripHeight * 0.14);
            var secondaryFontSize = Math.Max(7, labelFontSize - 1);
            var mappingFontSize = stripRows > 1
                ? Math.Max(7, metrics.StripHeight * 0.09)
                : Math.Max(8, metrics.StripHeight * 0.11);
            var badgeFontSize = Math.Max(7, mappingFontSize - 0.5);
            var segmentCorner = Math.Max(4, Math.Min(10, metrics.StripHeight / Math.Max(stripRows, 1) * 0.24));

            foreach (var segment in _stripSegments)
            {
                segment.Border.CornerRadius = new CornerRadius(segmentCorner);
                segment.FallbackIcon.Width = iconSize;
                segment.FallbackIcon.Height = iconSize;
                segment.Label.FontSize = labelFontSize;
                segment.BaseLabelFontSize = labelFontSize;
                segment.Label.MaxLines = stripRows > 1 ? 2 : 3;
                segment.Label.MaxWidth = Math.Max(
                    34,
                    (metrics.StageWidth - StripGrid.Padding.Left - StripGrid.Padding.Right
                        - Math.Max(0, stripCols - 1) * StripGrid.ColumnSpacing) / stripCols - 8);
                segment.SecondaryLabel.FontSize = secondaryFontSize;
                segment.SecondaryLabel.MaxWidth = segment.Label.MaxWidth;
                segment.Mapping.FontSize = mappingFontSize;
                segment.AvailabilityBadgeText.FontSize = badgeFontSize;
            }
        }

        DialRow.Spacing = metrics.DialSpacing;

        foreach (var slot in _slots)
        {
            slot.Width = metrics.DialDiameter;
            slot.Height = metrics.DialDiameter;
        }

        EncoderStack.Spacing = showStrip && showDials ? Math.Max(8, 16 * metrics.Scale) : 0;
    }

    private void RebuildDials(ProfileTemplate preset)
    {
        if (_viewModel is null || _profile is null)
            return;

        RebuildStripSegments(preset);

        DialRow.Children.Clear();
        _slots.Clear();

        var showDials = preset.HasDialSlots();
        StripBar.Visibility = preset.HasTouchStrip() ? Visibility.Visible : Visibility.Collapsed;
        DialRow.Visibility = showDials ? Visibility.Visible : Visibility.Collapsed;

        if (showDials)
        {
            for (var i = 0; i < preset.Dials; i++)
            {
                var coordinate = $"{i},0";
                var slot = new DialSlotControl();
                slot.Initialize(_viewModel, _side, _profile, coordinate, _pageId);
                DialRow.Children.Add(slot);
                _slots.Add(slot);
            }
        }

        RefreshStripSegments(_profile);
    }

    private void RebuildStripSegments(ProfileTemplate preset)
    {
        StripGrid.Children.Clear();
        StripGrid.RowDefinitions.Clear();
        StripGrid.ColumnDefinitions.Clear();
        _stripSegments.Clear();

        if (!preset.HasTouchStrip())
            return;

        var rows = Math.Max(1, preset.GetTouchStripRows());
        var cols = Math.Max(1, preset.GetTouchStripColumns());

        for (var c = 0; c < cols; c++)
            StripGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var r = 0; r < rows; r++)
            StripGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                var encoderColumn = preset.GetEncoderColumnForTouchStripCell(c, r);
                var encoderRow = preset.GetEncoderRowForTouchStripCell(c, r);
                var coordinate = $"{encoderColumn},{encoderRow}";
                var segment = CreateStripSegment(coordinate);
                Grid.SetColumn(segment.Border, c);
                Grid.SetRow(segment.Border, r);
                StripGrid.Children.Add(segment.Border);
                _stripSegments.Add(segment);
            }
        }
    }

    private StripSegmentSlot CreateStripSegment(string coordinate)
    {
        var mapping = new TextBlock
        {
            FontSize = 8,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Left,
            MaxLines = 1,
            Margin = new Thickness(4, 1, 4, 0),
            Opacity = 0.82,
            Visibility = Visibility.Collapsed
        };

        var badgeText = new TextBlock
        {
            FontSize = 7,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xF4, 0xFF, 0xD6, 0x95)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        };

        var badge = new Border
        {
            Background = SegmentWarningBadgeBrush,
            BorderBrush = SegmentWarningBadgeBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(0, 2, 2, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            Child = badgeText
        };

        var layoutCanvas = new Canvas
        {
            Width = 200,
            Height = 100
        };

        var layoutViewbox = new Viewbox
        {
            Child = layoutCanvas,
            Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(2, 10, 2, 2),
            Visibility = Visibility.Collapsed
        };

        var fallbackIcon = new Image
        {
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 10, 2, 2),
            Visibility = Visibility.Collapsed
        };

        var label = new TextBlock
        {
            FontSize = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            TextTrimming = TextTrimming.None,
            TextAlignment = TextAlignment.Center,
            MaxLines = 4,
            TextWrapping = TextWrapping.WrapWholeWords,
            Margin = new Thickness(2, 0, 2, 0)
        };

        var secondaryLabel = new TextBlock
        {
            FontSize = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            MaxLines = 1,
            Margin = new Thickness(2, 0, 2, 2),
            Opacity = 0.72,
            Visibility = Visibility.Collapsed
        };

        var labelStack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        labelStack.Children.Add(label);
        labelStack.Children.Add(secondaryLabel);

        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.Children.Add(mapping);
        content.Children.Add(badge);
        Grid.SetRow(layoutViewbox, 0);
        Grid.SetRow(fallbackIcon, 0);
        Grid.SetRow(labelStack, 1);
        content.Children.Add(layoutViewbox);
        content.Children.Add(fallbackIcon);
        content.Children.Add(labelStack);

        var border = new Border
        {
            BorderBrush = SegmentBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Background = SegmentBackgroundBrush,
            Padding = new Thickness(2),
            Child = content,
            Tag = coordinate,
            CanDrag = false,
            AllowDrop = true
        };

        border.DragStarting += OnStripSegmentDragStarting;
        border.DragOver += OnStripSegmentDragOver;
        border.DragLeave += OnStripSegmentDragLeave;
        border.Drop += OnStripSegmentDrop;
        border.PointerPressed += OnStripSegmentPointerPressed;

        return new StripSegmentSlot
        {
            Border = border,
            Mapping = mapping,
            AvailabilityBadge = badge,
            AvailabilityBadgeText = badgeText,
            LayoutViewbox = layoutViewbox,
            LayoutCanvas = layoutCanvas,
            FallbackIcon = fallbackIcon,
            Label = label,
            SecondaryLabel = secondaryLabel,
            Coordinate = coordinate
        };
    }

    private void RefreshStripSegments(ProfileArchive profile)
    {
        if (_viewModel is null)
            return;

        foreach (var segment in _stripSegments)
        {
            var action = profile.GetAction(ControllerKind.Encoder, segment.Coordinate, _pageId);
            if (action is null)
            {
                segment.Border.CanDrag = false;
                ApplyEmptyStripSegment(segment, profile.Preset);
                UpdateStripSegmentChrome(segment);
                continue;
            }

            segment.Mapping.Text = GetSegmentBadge(profile.Preset, segment.Coordinate);
            var model = _viewModel.TouchStripRenderer.BuildSegmentRender(profile, action, segment.Coordinate, _pageId);
            model.TooltipText = $"{BuildSegmentHeader(profile.Preset, segment.Coordinate)}\n{model.TooltipText}";
            segment.Border.CanDrag = true;
            ApplyRenderModel(segment, model);
            UpdateStripSegmentChrome(segment);
        }
    }

    private void ApplyRenderModel(StripSegmentSlot segment, TouchStripRenderModel model)
    {
        segment.Mapping.Visibility = Visibility.Visible;
        segment.AvailabilityBadgeText.Text = model.BadgeText;
        segment.AvailabilityBadge.Visibility = string.IsNullOrWhiteSpace(model.BadgeText)
            ? Visibility.Collapsed
            : Visibility.Visible;

        segment.Label.Text = model.PrimaryLabel;
        segment.Label.Opacity = 1.0;
        ConfigurePrimaryLabel(segment, model.PrimaryLabel);

        var showSecondary = !string.IsNullOrWhiteSpace(model.SecondaryLabel)
            && (model.PrimaryLabel?.Length ?? 0) <= 32;

        if (showSecondary)
        {
            segment.SecondaryLabel.Text = model.SecondaryLabel;
            segment.SecondaryLabel.Visibility = Visibility.Visible;
        }
        else
        {
            segment.SecondaryLabel.Visibility = Visibility.Collapsed;
        }

        segment.LayoutCanvas.Children.Clear();
        segment.LayoutViewbox.Visibility = Visibility.Collapsed;
        segment.FallbackIcon.Source = null;
        segment.FallbackIcon.Visibility = Visibility.Collapsed;

        if (model.Mode == TouchStripRenderMode.Layout && model.Layers.Count > 0)
        {
            RenderLayoutLayers(segment, model.Layers);
            segment.LayoutViewbox.Visibility = Visibility.Visible;
        }
        else if (!string.IsNullOrWhiteSpace(model.PrimaryIconPath))
        {
            segment.FallbackIcon.Source = GetImageSource(model.PrimaryIconPath!);
            segment.FallbackIcon.Visibility = segment.FallbackIcon.Source is not null
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        ToolTipService.SetToolTip(segment.Border, model.TooltipText);
    }

    private void ApplyEmptyStripSegment(StripSegmentSlot segment, ProfileTemplate preset)
    {
        segment.Mapping.Text = string.Empty;
        segment.Mapping.Visibility = Visibility.Collapsed;
        segment.AvailabilityBadgeText.Text = string.Empty;
        segment.AvailabilityBadge.Visibility = Visibility.Collapsed;
        segment.Label.Text = string.Empty;
        segment.SecondaryLabel.Text = string.Empty;
        segment.SecondaryLabel.Visibility = Visibility.Collapsed;

        segment.LayoutCanvas.Children.Clear();
        segment.LayoutViewbox.Visibility = Visibility.Collapsed;
        segment.FallbackIcon.Source = null;
        segment.FallbackIcon.Visibility = Visibility.Collapsed;

        ToolTipService.SetToolTip(segment.Border,
            $"{BuildSegmentHeader(preset, segment.Coordinate)}\nDrop an encoder action here.\nCoordinate: {segment.Coordinate}");
    }

    private void RenderLayoutLayers(StripSegmentSlot segment, IReadOnlyList<TouchStripRenderLayer> layers)
    {
        foreach (var layer in layers.OrderBy(l => l.ZOrder))
        {
            var element = CreateLayerElement(layer);
            if (element is null)
                continue;

            Canvas.SetLeft(element, layer.Rect.X);
            Canvas.SetTop(element, layer.Rect.Y);
            segment.LayoutCanvas.Children.Add(element);
        }
    }

    private UIElement? CreateLayerElement(TouchStripRenderLayer layer)
    {
        if (layer.Rect.Width <= 0 || layer.Rect.Height <= 0)
            return null;

        switch (layer.Type)
        {
            case "image":
            {
                var image = new Image
                {
                    Width = layer.Rect.Width,
                    Height = layer.Rect.Height,
                    Stretch = Stretch.Fill
                };

                image.Source = GetImageSource(layer.ImagePath);
                if (image.Source is null)
                    return null;

                if (TryCreateBrush(layer.Background, out var backgroundBrush))
                {
                    return new Border
                    {
                        Width = layer.Rect.Width,
                        Height = layer.Rect.Height,
                        Background = backgroundBrush,
                        Child = image
                    };
                }

                return image;
            }
            case "text":
            case "placeholder":
            {
                var layerText = layer.Text ?? string.Empty;
                var layerFontSize = ResolveLayerFontSize(layerText, layer.Rect.Width, layer.Rect.Height, layer.FontSize ?? 14);
                var layerMaxLines = ResolveLayerMaxLines(layer.Rect.Height, layerFontSize);

                var textBlock = new TextBlock
                {
                    Width = layer.Rect.Width,
                    Height = layer.Rect.Height,
                    Text = layerText,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xE8, 0xFF, 0xFF, 0xFF)),
                    FontSize = layerFontSize,
                    FontWeight = MapFontWeight(layer.FontWeight),
                    TextTrimming = TextTrimming.None,
                    MaxLines = layerMaxLines,
                    TextWrapping = layerText.Contains(' ') ? TextWrapping.WrapWholeWords : TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center
                };

                ApplyTextAlignment(textBlock, layer.Alignment);

                if (TryCreateBrush(layer.Background, out var backgroundBrush))
                {
                    return new Border
                    {
                        Width = layer.Rect.Width,
                        Height = layer.Rect.Height,
                        Background = backgroundBrush,
                        Child = textBlock
                    };
                }

                return textBlock;
            }
            default:
                return null;
        }
    }

    private ImageSource? GetImageSource(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        if (_imageSourceCache.TryGetValue(path, out var cached))
            return cached;

        try
        {
            ImageSource source;
            if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                source = new SvgImageSource(new Uri(path));
            else
                source = new BitmapImage(new Uri(path));

            _imageSourceCache[path] = source;
            return source;
        }
        catch
        {
            _imageSourceCache[path] = null;
            return null;
        }
    }

    private static bool TryCreateBrush(string? colorText, out Brush brush)
    {
        brush = SegmentLayerBackgroundBrush;
        if (string.IsNullOrWhiteSpace(colorText))
            return false;
        if (!TryParseColor(colorText!, out var color))
            return false;
        brush = new SolidColorBrush(color);
        return true;
    }

    private static bool TryParseColor(string text, out Windows.UI.Color color)
    {
        color = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        var normalized = text.Trim();
        if (!normalized.StartsWith('#'))
            return false;

        normalized = normalized[1..];
        if (normalized.Length == 6)
            normalized = $"FF{normalized}";

        if (normalized.Length != 8)
            return false;

        if (!byte.TryParse(normalized[0..2], System.Globalization.NumberStyles.HexNumber, null, out var a)
            || !byte.TryParse(normalized[2..4], System.Globalization.NumberStyles.HexNumber, null, out var r)
            || !byte.TryParse(normalized[4..6], System.Globalization.NumberStyles.HexNumber, null, out var g)
            || !byte.TryParse(normalized[6..8], System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return false;
        }

        color = Windows.UI.Color.FromArgb(a, r, g, b);
        return true;
    }

    private static Windows.UI.Text.FontWeight MapFontWeight(int? weight)
    {
        if (weight is null)
            return Microsoft.UI.Text.FontWeights.SemiBold;
        if (weight >= 700)
            return Microsoft.UI.Text.FontWeights.Bold;
        if (weight >= 600)
            return Microsoft.UI.Text.FontWeights.SemiBold;
        return Microsoft.UI.Text.FontWeights.Normal;
    }

    private static void ApplyTextAlignment(TextBlock textBlock, string? alignment)
    {
        var normalized = alignment?.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "left":
                textBlock.TextAlignment = TextAlignment.Left;
                break;
            case "right":
                textBlock.TextAlignment = TextAlignment.Right;
                break;
            case "center":
            default:
                textBlock.TextAlignment = TextAlignment.Center;
                break;
        }
    }

    private static string BuildSegmentHeader(ProfileTemplate preset, string coordinate)
    {
        var badge = GetSegmentBadge(preset, coordinate);
        return string.IsNullOrWhiteSpace(badge)
            ? "Touch strip segment"
            : $"Touch strip: {badge}";
    }

    private static string GetSegmentBadge(ProfileTemplate preset, string coordinate)
    {
        if (!TryParseCoordinate(coordinate, out var x, out var y))
            return string.Empty;

        var dialNumber = x + 1;
        if (preset.GetTouchStripRows() <= 1)
            return $"Dial {dialNumber}";

        if (string.Equals(preset.Id, "g100sd", StringComparison.OrdinalIgnoreCase))
            return y == 0 ? $"Dial {dialNumber} Top" : $"Dial {dialNumber} Bottom";

        return $"Dial {dialNumber} Row {y + 1}";
    }

    private static bool TryParseCoordinate(string coordinate, out int x, out int y)
    {
        x = 0;
        y = 0;
        var parts = coordinate.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2
            && int.TryParse(parts[0], out x)
            && int.TryParse(parts[1], out y);
    }

    private static void ConfigurePrimaryLabel(StripSegmentSlot segment, string? value)
    {
        var text = value ?? string.Empty;
        var hasSpaces = text.Contains(' ');
        var width = Math.Max(30, segment.Label.MaxWidth);
        var baseFont = segment.BaseLabelFontSize > 0 ? segment.BaseLabelFontSize : segment.Label.FontSize;
        var maxLines = Math.Max(segment.Label.MaxLines, 4);

        if (text.Length > 36)
            maxLines = Math.Max(maxLines, 5);
        if (text.Length > 52)
            maxLines = Math.Max(maxLines, 6);

        var font = baseFont;
        while (font > 6.5)
        {
            var estimatedLines = EstimateWrappedLines(text, width, font, hasSpaces);
            if (estimatedLines <= maxLines)
                break;
            font -= 0.5;
        }

        segment.Label.FontSize = Math.Max(6.5, font);
        segment.Label.MaxLines = maxLines;
        segment.Label.TextWrapping = hasSpaces ? TextWrapping.WrapWholeWords : TextWrapping.Wrap;
        segment.Label.TextTrimming = TextTrimming.None;
    }

    private static double ResolveLayerFontSize(string text, double width, double height, double preferred)
    {
        if (string.IsNullOrWhiteSpace(text))
            return preferred;

        var hasSpaces = text.Contains(' ');
        var font = preferred;
        while (font > 6.5)
        {
            var maxLines = ResolveLayerMaxLines(height, font);
            var estimatedLines = EstimateWrappedLines(text, width, font, hasSpaces);
            if (estimatedLines <= maxLines)
                break;
            font -= 0.5;
        }

        return Math.Max(6.5, font);
    }

    private static int ResolveLayerMaxLines(double height, double fontSize)
    {
        var lineHeight = Math.Max(1.0, fontSize * 1.2);
        return Math.Max(1, (int)Math.Floor(height / lineHeight));
    }

    private static int EstimateWrappedLines(string text, double width, double fontSize, bool hasSpaces)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 1;

        var avgCharWidth = Math.Max(1.0, fontSize * 0.52);
        var charsPerLine = Math.Max(3, (int)Math.Floor(width / avgCharWidth));

        if (!hasSpaces)
            return Math.Max(1, (int)Math.Ceiling(text.Length / (double)charsPerLine));

        var lines = 1;
        var current = 0;
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            var wordLength = word.Length;
            if (wordLength > charsPerLine)
            {
                if (current > 0)
                {
                    lines++;
                    current = 0;
                }

                lines += (int)Math.Ceiling(wordLength / (double)charsPerLine) - 1;
                current = wordLength % charsPerLine;
                if (current == 0)
                    current = charsPerLine;
                continue;
            }

            var needed = current == 0 ? wordLength : wordLength + 1;
            if (current + needed <= charsPerLine)
            {
                current += needed;
            }
            else
            {
                lines++;
                current = wordLength;
            }
        }

        return Math.Max(1, lines);
    }

    private void OnStripSegmentDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (_viewModel is null || _profile is null || sender is not Border border || border.Tag is not string coordinate)
            return;

        var action = _profile.GetAction(ControllerKind.Encoder, coordinate, _pageId);
        if (action is null)
            return;

        _viewModel.BeginDrag(_side, ControllerKind.Encoder, coordinate, action, _pageId);
        args.Data.SetText("profile-action");
        args.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void OnStripSegmentDragOver(object sender, DragEventArgs e)
    {
        if (_viewModel?.DragContext is null || _viewModel.DragContext.Controller != ControllerKind.Encoder)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Move;
        var segment = GetStripSegment(sender);
        if (segment is null || segment.IsDropTarget)
            return;

        segment.IsDropTarget = true;
        segment.Border.BorderBrush = SegmentDropHighlightBrush;
        segment.Border.Background = SegmentDropBackgroundBrush;
    }

    private void OnStripSegmentDragLeave(object sender, DragEventArgs e)
    {
        var segment = GetStripSegment(sender);
        if (segment is null || !segment.IsDropTarget)
            return;

        segment.IsDropTarget = false;
        UpdateStripSegmentChrome(segment);
    }

    private void OnStripSegmentDrop(object sender, DragEventArgs e)
    {
        var segment = GetStripSegment(sender);
        if (segment is null)
            return;

        segment.IsDropTarget = false;
        UpdateStripSegmentChrome(segment);

        if (_viewModel?.DragContext is null)
            return;

        _viewModel.DropAction(_side, ControllerKind.Encoder, segment.Coordinate);
    }

    private void OnStripSegmentPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not string coordinate)
            return;

        _selectedStripCoordinate = coordinate;
        _ = Focus(FocusState.Programmatic);
        UpdateAllStripSegmentChrome();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not (VirtualKey.Delete or VirtualKey.Back))
            return;

        if (string.IsNullOrWhiteSpace(_selectedStripCoordinate))
            return;

        _viewModel?.RemoveAction(_side, ControllerKind.Encoder, _selectedStripCoordinate);
        e.Handled = true;
    }

    private StripSegmentSlot? GetStripSegment(object sender)
    {
        if (sender is not Border border)
            return null;

        foreach (var segment in _stripSegments)
        {
            if (ReferenceEquals(segment.Border, border))
                return segment;
        }

        return null;
    }

    private void OnHostGotFocus(object sender, RoutedEventArgs e)
    {
        UpdateAllStripSegmentChrome();
    }

    private void OnHostLostFocus(object sender, RoutedEventArgs e)
    {
        _selectedStripCoordinate = null;
        UpdateAllStripSegmentChrome();
    }

    private void UpdateAllStripSegmentChrome()
    {
        foreach (var segment in _stripSegments)
            UpdateStripSegmentChrome(segment);
    }

    private void UpdateStripSegmentChrome(StripSegmentSlot segment)
    {
        if (segment.IsDropTarget)
        {
            segment.Border.BorderBrush = SegmentDropHighlightBrush;
            segment.Border.Background = SegmentDropBackgroundBrush;
            return;
        }

        var isSelected = FocusState != FocusState.Unfocused
            && !string.IsNullOrWhiteSpace(_selectedStripCoordinate)
            && string.Equals(segment.Coordinate, _selectedStripCoordinate, StringComparison.OrdinalIgnoreCase);

        if (isSelected)
        {
            segment.Border.BorderBrush = SegmentSelectedBrush;
            segment.Border.Background = SegmentSelectedBackgroundBrush;
            return;
        }

        segment.Border.BorderBrush = SegmentBorderBrush;
        segment.Border.Background = SegmentBackgroundBrush;
    }
}
