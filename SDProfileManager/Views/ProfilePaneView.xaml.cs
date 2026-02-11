using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SDProfileManager.Models;
using SDProfileManager.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.Storage;

namespace SDProfileManager.Views;

public sealed partial class ProfilePaneView : UserControl
{
    private WorkspaceViewModel? _viewModel;
    private PaneSide _side;
    private DeckCanvasView? _deckCanvas;
    private ProfileArchive? _observedProfile;
    private bool _isProfileDropTarget;

    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush ProfileDropBorderBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0x6E, 0xBD, 0xFF));

    public ProfilePaneView()
    {
        this.InitializeComponent();
        Unloaded += OnUnloaded;
    }

    public void Initialize(WorkspaceViewModel viewModel, PaneSide side)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        if (_observedProfile is not null)
            _observedProfile.PropertyChanged -= OnObservedProfilePropertyChanged;

        _viewModel = viewModel;
        _side = side;

        PaneTitleText.Text = side == PaneSide.Left ? "Source Profile" : "Target Profile";
        EmptyStateText.Text = side == PaneSide.Right
            ? "Open a profile, create an empty target, or drop a .streamDeckProfile file."
            : "Open or drop a .streamDeckProfile to inspect and move actions.";

        // Show "New Empty" button only on right pane
        NewEmptyButton.Visibility = side == PaneSide.Right ? Visibility.Visible : Visibility.Collapsed;

        // Wire commands
        OpenButton.Command = viewModel.OpenProfileCommand;
        OpenButton.CommandParameter = side;
        CloseButton.Command = viewModel.CloseProfileCommand;
        CloseButton.CommandParameter = side;
        SplitViewButton.Command = viewModel.SplitProfileViewCommand;
        SplitViewButton.CommandParameter = side;

        if (side == PaneSide.Right)
        {
            NewEmptyButton.Command = viewModel.CreateEmptyTargetCommand;
        }

        // Build the controls strip
        BuildControlsStrip();

        // Listen for property changes
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        RebindProfileObserver();
        RefreshAll();
    }

    private void BuildControlsStrip()
    {
        if (_viewModel is null) return;
        ControlsPanel.Children.Clear();

        if (_side == PaneSide.Right)
        {
            // Target profile name editor
            var nameBox = new TextBox
            {
                PlaceholderText = "Target profile name",
                MinWidth = 220,
                MaxWidth = 320,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Padding = new Thickness(8, 6, 8, 6)
            };
            nameBox.LostFocus += (s, e) => CommitProfileName(nameBox);
            nameBox.KeyDown += (s, e) =>
            {
                if (e.Key is not (VirtualKey.Enter or VirtualKey.GamepadA))
                    return;

                CommitProfileName(nameBox);
                e.Handled = true;
            };

            var nameStack = new StackPanel { Spacing = 4 };
            nameStack.Children.Add(CreateChipLabel("Profile Name"));
            nameStack.Children.Add(WrapInChipBorder(nameBox));
            nameStack.Tag = nameBox;
            ControlsPanel.Children.Add(nameStack);

            // Preset picker
            var presetCombo = new ComboBox
            {
                ItemsSource = _viewModel.Templates,
                DisplayMemberPath = "Label",
                SelectedValuePath = "Id",
                MinWidth = 180,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
            presetCombo.SelectionChanged += (s, e) =>
            {
                if (presetCombo.SelectedValue is string presetId)
                    _viewModel.UpdatePreset(_side, presetId);
            };
            var presetStack = new StackPanel { Spacing = 4 };
            presetStack.Children.Add(CreateChipLabel("Preset"));
            presetStack.Children.Add(WrapInChipBorder(presetCombo));
            ControlsPanel.Children.Add(presetStack);
        }
        else
        {
            // Device label (read-only)
            var deviceStack = new StackPanel { Spacing = 4 };
            deviceStack.Children.Add(CreateChipLabel("Device"));
            var deviceText = new TextBlock
            {
                Text = "--",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = FindResource("TextPrimaryBrush") as Microsoft.UI.Xaml.Media.Brush
                    ?? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                Padding = new Thickness(9, 8, 9, 8),
                MinWidth = 180
            };
            deviceStack.Children.Add(WrapInChipBorder(deviceText));
            deviceStack.Tag = deviceText;
            ControlsPanel.Children.Add(deviceStack);
        }

        if (_side == PaneSide.Left)
        {
            // Source Lock toggle
            var lockStack = new StackPanel { Spacing = 4 };
            lockStack.Children.Add(CreateChipLabel("Source Lock"));
            var lockPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var lockToggle = new ToggleSwitch
            {
                IsOn = _viewModel.LockSourceProfile,
                MinWidth = 0
            };
            var lockLabel = new TextBlock
            {
                Text = _viewModel.LockSourceProfile ? "Copy -> Target" : "Move -> Target",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            };
            lockToggle.Toggled += (s, e) =>
            {
                _viewModel.SetSourceLockCommand.Execute(lockToggle.IsOn);
                lockLabel.Text = lockToggle.IsOn ? "Copy -> Target" : "Move -> Target";
            };
            lockPanel.Children.Add(lockToggle);
            lockPanel.Children.Add(lockLabel);
            lockStack.Children.Add(lockPanel);
            lockStack.Tag = lockToggle;
            ControlsPanel.Children.Add(lockStack);
        }

        if (_side == PaneSide.Right)
        {
            // Open in SD button
            var openSdButton = new Button
            {
                Content = "Open in SD",
                Style = FindResource("PaneButtonStyle") as Style,
                Command = _viewModel.OpenTargetInStreamDeckCommand,
                IsEnabled = false,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            openSdButton.Tag = "opensd";
            ControlsPanel.Children.Add(openSdButton);
        }

        // Save As button
        var saveButton = new Button
        {
            Content = "Save As",
            Style = FindResource("PaneButtonProminentStyle") as Style,
            Command = _viewModel.SaveProfileCommand,
            CommandParameter = _side,
            IsEnabled = false,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        saveButton.Tag = "save";
        ControlsPanel.Children.Add(saveButton);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceViewModel.LeftProfile) or nameof(WorkspaceViewModel.RightProfile))
            RebindProfileObserver();

        var isThisPanePageUpdate =
            (_side == PaneSide.Left && e.PropertyName == nameof(WorkspaceViewModel.LeftViewPageId))
            || (_side == PaneSide.Right && e.PropertyName == nameof(WorkspaceViewModel.RightViewPageId));

        if (isThisPanePageUpdate
            || e.PropertyName is nameof(WorkspaceViewModel.LeftProfile)
                or nameof(WorkspaceViewModel.RightProfile)
                or nameof(WorkspaceViewModel.LockSourceProfile)
                or nameof(WorkspaceViewModel.LeftPreflightReport)
                or nameof(WorkspaceViewModel.RightPreflightReport)
                or nameof(WorkspaceViewModel.LayoutMode))
        {
            DispatcherQueue.TryEnqueue(RefreshAll);
        }
    }

    private void RefreshAll()
    {
        if (_viewModel is null) return;
        RebindProfileObserver();

        var profile = _viewModel.GetProfile(_side);
        var hasProfile = profile is not null;

        // Update subtitle
        PaneSubtitleText.Text = profile?.DisplayName ?? "No profile loaded";
        CloseButton.IsEnabled = hasProfile;
        SplitViewButton.IsEnabled = hasProfile;
        var isSingleProfile = _viewModel.IsSingleProfileMode && _viewModel.IsSharedProfileView;
        SplitViewButton.Content = isSingleProfile ? "Unsplit" : "Split View";
        ModeChip.Visibility = isSingleProfile ? Visibility.Visible : Visibility.Collapsed;

        // Show/hide deck vs empty state
        EmptyState.Visibility = hasProfile ? Visibility.Collapsed : Visibility.Visible;
        DeckStage.Visibility = hasProfile ? Visibility.Visible : Visibility.Collapsed;

        // Update or create deck canvas
        if (hasProfile)
        {
            if (_deckCanvas is null)
            {
                _deckCanvas = new DeckCanvasView();
                _deckCanvas.Initialize(_viewModel, _side);
                DeckContainer.Children.Clear();
                DeckContainer.Children.Add(_deckCanvas);
            }
            _deckCanvas.SetProfile(profile);
        }
        else
        {
            DeckContainer.Children.Clear();
            _deckCanvas = null;
        }

        // Update controls strip values
        UpdateControlsStrip(profile);

        // Update preflight
        UpdatePreflight();
    }

    private void RebindProfileObserver()
    {
        if (_viewModel is null)
            return;

        var nextProfile = _viewModel.GetProfile(_side);
        if (ReferenceEquals(_observedProfile, nextProfile))
            return;

        if (_observedProfile is not null)
            _observedProfile.PropertyChanged -= OnObservedProfilePropertyChanged;

        _observedProfile = nextProfile;

        if (_observedProfile is not null)
            _observedProfile.PropertyChanged += OnObservedProfilePropertyChanged;
    }

    private void OnObservedProfilePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProfileArchive.PageOrder)
            or nameof(ProfileArchive.PageStates)
            or nameof(ProfileArchive.Preset)
            or nameof(ProfileArchive.Name))
        {
            DispatcherQueue.TryEnqueue(RefreshAll);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        if (_observedProfile is not null)
            _observedProfile.PropertyChanged -= OnObservedProfilePropertyChanged;
    }

    private void UpdateControlsStrip(ProfileArchive? profile)
    {
        foreach (var child in ControlsPanel.Children)
        {
            if (child is StackPanel stack)
            {
                var label = GetChipLabel(stack);
                if (label == "Device" && stack.Tag is TextBlock deviceText)
                {
                    deviceText.Text = profile?.Preset.Label ?? "--";
                }
                else if (label == "Preset")
                {
                    var combo = FindChild<ComboBox>(stack);
                    if (combo is not null)
                    {
                        combo.SelectedValue = profile?.Preset.Id;
                        combo.IsEnabled = profile is not null;
                    }
                }
                else if (label == "Profile Name" && stack.Tag is TextBox nameBox)
                {
                    if (nameBox.FocusState == FocusState.Unfocused)
                    {
                        var nextName = profile?.Name ?? string.Empty;
                        if (!string.Equals(nameBox.Text, nextName, StringComparison.Ordinal))
                            nameBox.Text = nextName;
                    }
                    nameBox.IsEnabled = profile is not null;
                }
            }
            else if (child is Button btn)
            {
                var tag = btn.Tag as string;
                if (tag is "save" or "opensd")
                    btn.IsEnabled = profile is not null;
            }
        }
    }

    private void UpdatePreflight()
    {
        if (_viewModel is null) return;
        PreflightPanel.Children.Clear();

        var report = _side == PaneSide.Left ? _viewModel.LeftPreflightReport : _viewModel.RightPreflightReport;
        if (report is null) return;

        // Summary toggle
        var summaryBorder = new Border
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(0x2E, 0x00, 0x00, 0x00)),
            BorderBrush = FindResource("SubtleBorderBrush") as Microsoft.UI.Xaml.Media.Brush
                ?? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 8, 10, 8)
        };

        var summaryGrid = new Grid();
        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var toggleBtn = new Button
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var togglePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var chevron = new FontIcon { Glyph = "\uE76C", FontSize = 10 };
        togglePanel.Children.Add(chevron);
        togglePanel.Children.Add(new TextBlock
        {
            Text = "Import Preflight",
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(0xDB, 0xFF, 0xFF, 0xFF))
        });
        toggleBtn.Content = togglePanel;
        Grid.SetColumn(toggleBtn, 0);
        summaryGrid.Children.Add(toggleBtn);

        var tintBrush = GetPreflightTintBrush(report);
        var summaryText = new TextBlock
        {
            Text = report.Summary,
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = tintBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(summaryText, 1);
        summaryGrid.Children.Add(summaryText);

        summaryBorder.Child = summaryGrid;

        // Detail card (collapsed initially)
        var detailCard = CreatePreflightDetailCard(report, tintBrush);
        detailCard.Visibility = Visibility.Collapsed;

        toggleBtn.Click += (s, e) =>
        {
            if (detailCard.Visibility == Visibility.Visible)
            {
                detailCard.Visibility = Visibility.Collapsed;
                chevron.Glyph = "\uE76C"; // chevron right
            }
            else
            {
                detailCard.Visibility = Visibility.Visible;
                chevron.Glyph = "\uE70D"; // chevron down
            }
        };

        PreflightPanel.Children.Add(summaryBorder);
        PreflightPanel.Children.Add(detailCard);
    }

    private static Border CreatePreflightDetailCard(PreflightReport report, Microsoft.UI.Xaml.Media.Brush tintBrush)
    {
        var border = new Border
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(0x33, 0x00, 0x00, 0x00)),
            BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(0x73, 0xFF, 0x6B, 0x6B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 8, 10, 8)
        };

        if (tintBrush is Microsoft.UI.Xaml.Media.SolidColorBrush scb)
        {
            border.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(0x73, scb.Color.R, scb.Color.G, scb.Color.B));
        }

        var stack = new StackPanel { Spacing = 6 };

        // Header
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var headerLabel = new TextBlock
        {
            Text = "Import Preflight",
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(0xDB, 0xFF, 0xFF, 0xFF))
        };
        Grid.SetColumn(headerLabel, 0);
        headerGrid.Children.Add(headerLabel);

        var summaryLabel = new TextBlock
        {
            Text = report.Summary,
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = tintBrush
        };
        Grid.SetColumn(summaryLabel, 1);
        headerGrid.Children.Add(summaryLabel);
        stack.Children.Add(headerGrid);

        if (report.IsClean)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "No structural issues detected in this profile.",
                FontSize = 11,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(0x9E, 0xFF, 0xFF, 0xFF))
            });
        }
        else
        {
            var shown = 0;
            foreach (var issue in report.Issues)
            {
                if (shown >= 3) break;
                stack.Children.Add(new TextBlock
                {
                    Text = $"[{issue.Severity.ToCode()}] {issue.Message}",
                    FontSize = 11,
                    MaxLines = 1,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Windows.UI.Color.FromArgb(0xAE, 0xFF, 0xFF, 0xFF))
                });
                shown++;
            }
            if (report.Issues.Count > 3)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = $"+{report.Issues.Count - 3} more",
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Windows.UI.Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF))
                });
            }
        }

        border.Child = stack;
        return border;
    }

    private static Microsoft.UI.Xaml.Media.SolidColorBrush GetPreflightTintBrush(PreflightReport report)
    {
        if (report.ErrorCount > 0)
            return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0x6B, 0x6B));
        if (report.WarningCount > 0)
            return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xD1, 0x69));
        return new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x5C, 0xD7, 0x94));
    }

    // --- Helper methods for building controls ---

    private static TextBlock CreateChipLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 10,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(0x85, 0xFF, 0xFF, 0xFF))
        };
    }

    private static Border WrapInChipBorder(UIElement content)
    {
        return new Border
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(0x40, 0x00, 0x00, 0x00)),
            BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = content
        };
    }

    private static string? GetChipLabel(StackPanel stack)
    {
        if (stack.Children.Count > 0 && stack.Children[0] is TextBlock tb)
            return tb.Text;
        return null;
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var result = FindChild<T>(child);
            if (result is not null) return result;
        }
        return null;
    }

    private void CommitProfileName(TextBox nameBox)
    {
        if (_viewModel is null || _side != PaneSide.Right)
            return;

        _viewModel.UpdateProfileName(_side, nameBox.Text);

        var profile = _viewModel.GetProfile(_side);
        if (profile is not null && !string.Equals(nameBox.Text, profile.Name, StringComparison.Ordinal))
            nameBox.Text = profile.Name;
    }

    private void OnProfileDragOver(object sender, DragEventArgs e)
    {
        if (!CanAcceptProfileDrop(e.DataView))
        {
            SetProfileDropTarget(false);
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = _side == PaneSide.Left
            ? "Drop to load source profile"
            : "Drop to load target profile";
        e.DragUIOverride.IsCaptionVisible = true;
        SetProfileDropTarget(true);
    }

    private void OnProfileDragLeave(object sender, DragEventArgs e) => SetProfileDropTarget(false);

    private async void OnProfileDrop(object sender, DragEventArgs e)
    {
        SetProfileDropTarget(false);

        if (_viewModel is null || !e.DataView.Contains(StandardDataFormats.StorageItems))
            return;

        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var file = items
                .OfType<StorageFile>()
                .FirstOrDefault(f => string.Equals(f.FileType, ".streamDeckProfile", StringComparison.OrdinalIgnoreCase));

            if (file is null)
            {
                _viewModel.Status = "Drop ignored: choose a .streamDeckProfile file.";
                return;
            }

            _viewModel.LoadProfileFromPath(_side, file.Path);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private static bool CanAcceptProfileDrop(DataPackageView dataView)
        => dataView.Contains(StandardDataFormats.StorageItems);

    private void SetProfileDropTarget(bool isActive)
    {
        if (_isProfileDropTarget == isActive) return;
        _isProfileDropTarget = isActive;

        if (isActive)
        {
            PaneRootBorder.BorderBrush = ProfileDropBorderBrush;
            return;
        }

        PaneRootBorder.BorderBrush = FindResource("SubtleBorderBrush") as Microsoft.UI.Xaml.Media.Brush
            ?? new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
    }

    /// <summary>
    /// Looks up a resource by key, walking into MergedDictionaries.
    /// WinUI 3's C# indexer on ResourceDictionary does NOT search MergedDictionaries automatically.
    /// </summary>
    private static object? FindResource(string key)
    {
        var rd = Application.Current.Resources;
        if (rd.ContainsKey(key)) return rd[key];
        foreach (var merged in rd.MergedDictionaries)
        {
            if (merged.ContainsKey(key)) return merged[key];
        }
        return null;
    }
}
