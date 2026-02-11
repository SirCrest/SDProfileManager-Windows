using System.Diagnostics;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDProfileManager.Helpers;
using SDProfileManager.Models;
using SDProfileManager.Services;
using Windows.Storage.Pickers;

namespace SDProfileManager.ViewModels;

public partial class WorkspaceViewModel : ObservableObject
{
    public const int MaxSupportedPages = 10;

    [ObservableProperty] private ProfileArchive? _leftProfile;
    [ObservableProperty] private ProfileArchive? _rightProfile;
    [ObservableProperty] private WorkspaceLayoutMode _layoutMode = WorkspaceLayoutMode.DualProfile;
    [ObservableProperty] private string _leftViewPageId = "";
    [ObservableProperty] private string _rightViewPageId = "";
    [ObservableProperty] private bool _lockSourceProfile = true;
    [ObservableProperty] private string _status = "Open source and target profiles.";
    [ObservableProperty] private DragContext? _dragContext;
    [ObservableProperty] private PreflightReport? _leftPreflightReport;
    [ObservableProperty] private PreflightReport? _rightPreflightReport;
    [ObservableProperty] private bool _canUndo;
    [ObservableProperty] private bool _canRedo;

    public IReadOnlyList<ProfileTemplate> Templates => ProfileTemplates.All;

    private readonly ProfileArchiveService _archiveService = new();
    private readonly ImageCacheService _imageCacheService = new();
    private readonly PluginCatalogService _pluginCatalogService = new();
    private readonly TouchStripLayoutService _touchStripLayoutService = new();
    private readonly TouchStripRenderService _touchStripRenderService;
    private const string UpdateUrl = "https://github.com/SirCrest/SDProfileManager/releases";

    private const int MaxHistoryDepth = 80;
    private readonly List<WorkspaceHistorySnapshot> _undoStack = [];
    private readonly List<WorkspaceHistorySnapshot> _redoStack = [];
    private readonly List<string> _leftFolderNavigation = [];
    private readonly List<string> _rightFolderNavigation = [];
    private bool _isApplyingHistory;

    public WorkspaceViewModel()
    {
        _touchStripRenderService = new TouchStripRenderService(_pluginCatalogService, _touchStripLayoutService);
    }

    public ImageCacheService ImageCache => _imageCacheService;
    public PluginCatalogService PluginCatalog => _pluginCatalogService;
    public TouchStripLayoutService TouchStripLayouts => _touchStripLayoutService;
    public TouchStripRenderService TouchStripRenderer => _touchStripRenderService;
    public bool IsSingleProfileMode => LayoutMode == WorkspaceLayoutMode.SingleProfile;
    public bool IsSharedProfileView => LeftProfile is not null && ReferenceEquals(LeftProfile, RightProfile);

    partial void OnLayoutModeChanged(WorkspaceLayoutMode value)
    {
        OnPropertyChanged(nameof(IsSingleProfileMode));
    }

    partial void OnLeftProfileChanged(ProfileArchive? value)
    {
        EnsurePaneViewPage(PaneSide.Left, updateProfileActivePage: false);
        if (value is null)
            LeftViewPageId = "";

        OnPropertyChanged(nameof(IsSharedProfileView));
        EnsureLayoutModeValidity();
    }

    partial void OnRightProfileChanged(ProfileArchive? value)
    {
        EnsurePaneViewPage(PaneSide.Right, updateProfileActivePage: false);
        if (value is null)
            RightViewPageId = "";

        OnPropertyChanged(nameof(IsSharedProfileView));
        EnsureLayoutModeValidity();
    }

    [RelayCommand]
    public void SetSourceLock(bool isLocked)
    {
        if (LockSourceProfile == isLocked) return;
        RecordHistorySnapshot();
        LockSourceProfile = isLocked;
        if (IsSingleProfileMode && IsSharedProfileView)
            Status = "Source lock updated (single profile mode always moves actions).";
        else
            Status = isLocked ? "Source lock enabled: drag to target copies." : "Source lock disabled: drag to target moves.";
        AppLog.Info($"Source lock updated lock={isLocked}");
    }

    [RelayCommand]
    public void SplitProfileView(PaneSide anchorSide)
    {
        var anchorProfile = GetProfile(anchorSide);
        if (anchorProfile is null)
        {
            Status = "Load a profile first.";
            return;
        }

        RecordHistorySnapshot();

        if (IsSingleProfileMode && IsSharedProfileView)
        {
            DisableSingleProfileMode(anchorSide, cloneSharedProfile: true, updateStatus: false);
            Status = "Single profile mode disabled.";
            AppLog.Info($"Disabled single profile mode keep={anchorSide}");
            return;
        }

        EnableSingleProfileMode(anchorSide);
        Status = "Single profile mode enabled.";
        AppLog.Info($"Enabled single profile mode anchor={anchorSide}");
    }

    [RelayCommand]
    public void Undo()
    {
        if (_undoStack.Count == 0)
        {
            Status = "Nothing to undo.";
            return;
        }

        var previous = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);

        var current = CaptureHistorySnapshot();
        _redoStack.Add(current);
        if (_redoStack.Count > MaxHistoryDepth)
            _redoStack.RemoveRange(0, _redoStack.Count - MaxHistoryDepth);

        ApplyHistorySnapshot(previous);
        Status = "Undo complete.";
        AppLog.Info("Undo applied.");
        RefreshHistoryAvailability();
    }

    [RelayCommand]
    public void Redo()
    {
        if (_redoStack.Count == 0)
        {
            Status = "Nothing to redo.";
            return;
        }

        var next = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);

        var current = CaptureHistorySnapshot();
        _undoStack.Add(current);
        if (_undoStack.Count > MaxHistoryDepth)
            _undoStack.RemoveRange(0, _undoStack.Count - MaxHistoryDepth);

        ApplyHistorySnapshot(next);
        Status = "Redo complete.";
        AppLog.Info("Redo applied.");
        RefreshHistoryAvailability();
    }

    [RelayCommand]
    public async Task OpenProfile(PaneSide side)
    {
        var picker = new FileOpenPicker();
        WindowHelper.InitializePicker(picker);
        picker.FileTypeFilter.Add(".streamDeckProfile");

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            Status = "Open canceled.";
            AppLog.Info($"Open profile canceled side={side}");
            return;
        }

        LoadProfileFromPath(side, file.Path);
    }

    public bool LoadProfileFromPath(PaneSide side, string profilePath)
    {
        if (string.IsNullOrWhiteSpace(profilePath))
        {
            Status = "Invalid profile path.";
            return false;
        }

        if (!string.Equals(Path.GetExtension(profilePath), ".streamDeckProfile", StringComparison.OrdinalIgnoreCase))
        {
            Status = "Only .streamDeckProfile files are supported.";
            AppLog.Info($"Ignored non-profile file side={side} path={profilePath}");
            return false;
        }

        try
        {
            var archive = _archiveService.LoadProfile(profilePath);
            RecordHistorySnapshot();
            SetProfileForPane(side, archive, archive.ActivePageId);
            EnsureLayoutModeValidity();
            EnsurePaneViewPage(PaneSide.Left, updateProfileActivePage: false);
            EnsurePaneViewPage(PaneSide.Right, updateProfileActivePage: false);

            if (IsSharedProfileView)
                RefreshPreflightReports();
            else
                RefreshPreflight(side);

            Status = $"Loaded {Path.GetFileName(profilePath)}.";
            AppLog.Info($"Loaded profile side={side} file={Path.GetFileName(profilePath)}");
            return true;
        }
        catch (Exception ex)
        {
            Status = $"Failed to load profile: {ex.Message}";
            AppLog.Error($"Failed loading profile side={side} file={profilePath} error={ex}");
            return false;
        }
    }

    [RelayCommand]
    public void CreateEmptyTarget()
    {
        var baseTemplate = RightProfile?.Preset ?? LeftProfile?.Preset ?? ProfileTemplates.GetTemplate("20GBX9901");

        try
        {
            var archive = _archiveService.CreateEmptyProfile(baseTemplate);
            RecordHistorySnapshot();
            SetProfileForPane(PaneSide.Right, archive, archive.ActivePageId);
            EnsureLayoutModeValidity();
            RefreshPreflight(PaneSide.Right);
            Status = $"Created empty target profile ({baseTemplate.Label}).";
            AppLog.Info($"Created empty target preset={baseTemplate.Id}");
        }
        catch (Exception ex)
        {
            Status = $"Failed to create target profile: {ex.Message}";
            AppLog.Error($"Failed creating empty target error={ex.Message}");
        }
    }

    public void UpdatePreset(PaneSide side, string presetId)
    {
        if (!ProfileTemplates.ById.TryGetValue(presetId, out var template)) return;
        var profile = GetProfile(side);
        if (profile is null || profile.Preset.Id == template.Id) return;

        RecordHistorySnapshot();
        profile.Preset = template;
        profile.PackageManifest.DeviceModel = template.DeviceModel;
        PruneActions(template, profile);
        profile.UpdatePresetControllersForAllPages();
        if (IsSharedProfileView)
            RefreshPreflightReports();
        else
            RefreshPreflight(side);
        Status = side == PaneSide.Left
            ? $"Set source preset to {template.Label}."
            : $"Set target preset to {template.Label}.";
    }

    public void SelectPage(PaneSide side, string pageId)
    {
        var profile = GetProfile(side);
        if (profile is null) return;

        var currentPageId = GetViewPageId(side);
        var resolvedPageId = ResolvePanePageId(profile, pageId);
        if (string.Equals(currentPageId, resolvedPageId, StringComparison.OrdinalIgnoreCase))
            return;

        RecordHistorySnapshot();
        if (IsVisibleTopLevelPage(profile, resolvedPageId))
            ResetFolderNavigation(side);
        SetPaneViewPage(side, resolvedPageId, updateProfileActivePage: true);
        Status = $"Switched {(side == PaneSide.Left ? "source" : "target")} page.";
        AppLog.Info($"Switched page side={side} page={resolvedPageId}");
    }

    public void AddPage(PaneSide side)
    {
        var profile = GetProfile(side);
        if (profile is null) return;
        if (profile.PageOrder.Count >= MaxSupportedPages)
        {
            Status = $"Page limit reached ({MaxSupportedPages}).";
            AppLog.Info($"Add page blocked side={side} reason=max-pages");
            return;
        }

        RecordHistorySnapshot();
        var createdPageId = profile.CreatePage();
        ResetFolderNavigation(side);
        SetPaneViewPage(side, createdPageId, updateProfileActivePage: true);
        EnsurePaneViewPage(PaneSide.Left, updateProfileActivePage: false);
        EnsurePaneViewPage(PaneSide.Right, updateProfileActivePage: false);
        if (IsSharedProfileView)
            RefreshPreflightReports();
        else
            RefreshPreflight(side);
        Status = $"Added page to {(side == PaneSide.Left ? "source" : "target")} profile.";
        AppLog.Info($"Added page side={side} page={createdPageId}");
    }

    public void UpdateProfileName(PaneSide side, string? requestedName)
    {
        var profile = GetProfile(side);
        if (profile is null) return;

        var trimmed = (requestedName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            trimmed = "Untitled Profile";

        if (string.Equals(profile.Name.Trim(), trimmed, StringComparison.Ordinal))
            return;

        RecordHistorySnapshot();
        profile.Name = trimmed;
        Status = $"Renamed {(side == PaneSide.Left ? "source" : "target")} profile.";
        AppLog.Info($"Renamed profile side={side} name={trimmed}");
    }

    public void RemoveAction(PaneSide side, ControllerKind controller, string coordinate)
    {
        var profile = GetProfile(side);
        if (profile is null) return;

        var pageId = GetViewPageId(side);
        if (string.IsNullOrWhiteSpace(pageId))
            return;

        var existing = profile.GetAction(controller, coordinate, pageId);
        if (existing is null)
            return;

        RecordHistorySnapshot();
        profile.RemoveAction(controller, coordinate, pageId);

        if (DragContext is not null
            && DragContext.SourceSide == side
            && DragContext.Controller == controller
            && string.Equals(DragContext.SourcePageId, pageId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(DragContext.Coordinate, coordinate, StringComparison.OrdinalIgnoreCase))
        {
            DragContext = null;
        }

        if (IsSharedProfileView)
            RefreshPreflightReports();
        else
            RefreshPreflight(side);

        var actionType = controller == ControllerKind.Keypad ? "key" : "dial";
        Status = $"Deleted {actionType} action.";
        AppLog.Info($"Deleted action side={side} controller={controller} coordinate={coordinate} page={pageId}");
    }

    public void RemovePage(PaneSide side, string pageId)
    {
        var profile = GetProfile(side);
        if (profile is null) return;

        var normalizedPageId = ProfileArchive.NormalizePageId(pageId);
        var visiblePageIds = profile.PageOrder.Count > 0
            ? ProfileArchive.UniquePageIds(profile.PageOrder)
            : [ProfileArchive.NormalizePageId(profile.ActivePageId)];

        if (visiblePageIds.Count <= 1)
        {
            Status = "Cannot delete the last page.";
            AppLog.Info($"Remove page blocked side={side} page={normalizedPageId} reason=last-page");
            return;
        }

        if (profile.GetPageState(normalizedPageId) is null)
        {
            Status = "Page remove failed.";
            AppLog.Warn($"Remove page failed side={side} page={normalizedPageId} reason=missing-page");
            return;
        }

        RecordHistorySnapshot();
        var removed = profile.RemovePage(normalizedPageId);
        if (!removed)
        {
            Status = "Page remove failed.";
            AppLog.Warn($"Remove page failed side={side} page={normalizedPageId}");
            return;
        }

        if (DragContext is not null
            && DragContext.SourceSide == side
            && string.Equals(ProfileArchive.NormalizePageId(DragContext.SourcePageId), normalizedPageId, StringComparison.OrdinalIgnoreCase))
        {
            DragContext = null;
        }

        PruneFolderNavigation(PaneSide.Left);
        PruneFolderNavigation(PaneSide.Right);
        EnsurePaneViewPage(PaneSide.Left, updateProfileActivePage: false);
        EnsurePaneViewPage(PaneSide.Right, updateProfileActivePage: false);
        var currentPanePageId = GetViewPageId(side);
        if (!string.IsNullOrWhiteSpace(currentPanePageId))
            profile.SetActivePage(currentPanePageId);

        if (IsSharedProfileView)
            RefreshPreflightReports();
        else
            RefreshPreflight(side);
        Status = $"Removed page from {(side == PaneSide.Left ? "source" : "target")} profile.";
        AppLog.Info($"Removed page side={side} page={normalizedPageId}");
    }

    [RelayCommand]
    public void CloseProfile(PaneSide side)
    {
        var profile = GetProfile(side);
        if (profile is null)
        {
            Status = $"No {(side == PaneSide.Left ? "source" : "target")} profile loaded.";
            return;
        }

        RecordHistorySnapshot();

        if (side == PaneSide.Left)
        {
            LeftProfile = null;
            LeftViewPageId = "";
            LeftPreflightReport = null;
            ResetFolderNavigation(PaneSide.Left);
        }
        else
        {
            RightProfile = null;
            RightViewPageId = "";
            RightPreflightReport = null;
            ResetFolderNavigation(PaneSide.Right);
        }

        if (DragContext?.SourceSide == side)
            DragContext = null;

        EnsureLayoutModeValidity();
        Status = $"Closed {(side == PaneSide.Left ? "source" : "target")} profile.";
        AppLog.Info($"Closed profile side={side} previous={profile.DisplayName}");
    }

    public void OpenFolderAction(PaneSide side, ControllerKind controller, string coordinate)
    {
        if (controller != ControllerKind.Keypad)
            return;

        var profile = GetProfile(side);
        if (profile is null)
            return;

        var sourcePageId = GetViewPageId(side);
        if (string.IsNullOrWhiteSpace(sourcePageId))
            return;

        var action = profile.GetAction(controller, coordinate, sourcePageId);
        if (action is null)
            return;

        var folderTargetId = ProfileArchive.NormalizePageId(
            action.GetProperty("Settings")?.GetProperty("ProfileUUID").GetStringValue() ?? "");
        if (string.IsNullOrWhiteSpace(folderTargetId))
            return;

        if (profile.GetPageState(folderTargetId) is null)
        {
            Status = "Folder target is missing in this profile.";
            AppLog.Warn($"Open folder failed side={side} sourcePage={sourcePageId} target={folderTargetId} reason=missing-page");
            return;
        }

        if (string.Equals(folderTargetId, sourcePageId, StringComparison.OrdinalIgnoreCase))
            return;

        var stack = GetFolderNavigationStack(side);
        if (stack.Count == 0 || !string.Equals(stack[^1], sourcePageId, StringComparison.OrdinalIgnoreCase))
            stack.Add(sourcePageId);

        SetPaneViewPage(side, folderTargetId, updateProfileActivePage: true);

        if (IsSharedProfileView)
            RefreshPreflightReports();
        else
            RefreshPreflight(side);

        var folderName = profile.GetPageState(folderTargetId)?.Manifest.Name?.Trim();
        Status = string.IsNullOrWhiteSpace(folderName)
            ? "Opened folder page."
            : $"Opened folder {folderName}.";
        AppLog.Info($"Opened folder side={side} sourcePage={sourcePageId} targetPage={folderTargetId}");
    }

    public bool CanNavigateFolderBack(PaneSide side)
    {
        PruneFolderNavigation(side);
        return GetFolderNavigationStack(side).Count > 0;
    }

    public void NavigateFolderBack(PaneSide side)
    {
        var profile = GetProfile(side);
        if (profile is null)
            return;

        var stack = GetFolderNavigationStack(side);
        while (stack.Count > 0)
        {
            var previousPage = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            if (profile.GetPageState(previousPage) is null)
                continue;

            SetPaneViewPage(side, previousPage, updateProfileActivePage: true);
            if (IsSharedProfileView)
                RefreshPreflightReports();
            else
                RefreshPreflight(side);
            Status = "Returned from folder.";
            AppLog.Info($"Closed folder view side={side} page={previousPage}");
            return;
        }

        Status = "No folder history on this pane.";
    }

    public void BeginDrag(PaneSide side, ControllerKind controller, string coordinate, JsonNode action, string pageId)
    {
        DragContext = new DragContext(side, pageId, controller, coordinate, action.DeepClone());
    }

    public void DropAction(PaneSide side, ControllerKind controller, string coordinate)
    {
        var drag = DragContext;
        if (drag is null) return;

        if (drag.Controller != controller)
        {
            Status = "Drop canceled: keys and dials cannot be mixed.";
            DragContext = null;
            return;
        }

        var sourceProfile = GetProfile(drag.SourceSide);
        var targetProfile = GetProfile(side);
        if (sourceProfile is null || targetProfile is null)
        {
            DragContext = null;
            return;
        }

        var sourcePageId = drag.SourcePageId;
        var targetPageId = GetViewPageId(side);
        if (string.IsNullOrWhiteSpace(targetPageId))
        {
            DragContext = null;
            return;
        }

        var isSharedProfileMove = ReferenceEquals(sourceProfile, targetProfile);

        // Same slot, same page, same pane: no-op.
        if (drag.SourceSide == side
            && string.Equals(sourcePageId, targetPageId, StringComparison.OrdinalIgnoreCase)
            && drag.Coordinate == coordinate)
        {
            DragContext = null;
            return;
        }

        RecordHistorySnapshot();

        // Same pane move.
        if (drag.SourceSide == side)
        {
            sourceProfile.RemoveAction(drag.Controller, drag.Coordinate, sourcePageId);
            sourceProfile.SetAction(drag.Action, controller, coordinate, targetPageId);
            MergeRequiredPlugin(drag.Action, sourceProfile);
            _archiveService.CopyReferencedFiles(drag.Action, sourceProfile, sourceProfile, sourcePageId, targetPageId);
            SetPaneViewPage(side, targetPageId, updateProfileActivePage: true);
            if (IsSharedProfileView)
                RefreshPreflightReports();
            else
                RefreshPreflight(side);
            Status = $"Moved action to {coordinate}.";
            AppLog.Info($"Moved action same-pane side={side} sourcePage={sourcePageId} targetPage={targetPageId} coordinate={coordinate}");
            DragContext = null;
            return;
        }

        // Cross-pane.
        var shouldCopyOnly = !isSharedProfileMove
            && LockSourceProfile
            && drag.SourceSide == PaneSide.Left
            && side == PaneSide.Right;
        if (!shouldCopyOnly)
            sourceProfile.RemoveAction(drag.Controller, drag.Coordinate, sourcePageId);

        targetProfile.SetAction(drag.Action, controller, coordinate, targetPageId);
        MergeRequiredPlugin(drag.Action, targetProfile);
        _archiveService.CopyReferencedFiles(drag.Action, sourceProfile, targetProfile, sourcePageId, targetPageId);
        SetPaneViewPage(side, targetPageId, updateProfileActivePage: true);

        if (isSharedProfileMove)
        {
            RefreshPreflightReports();
            Status = "Moved action within single profile.";
            AppLog.Info($"Moved action shared-profile sourcePage={sourcePageId} targetPage={targetPageId} coordinate={coordinate}");
            DragContext = null;
            return;
        }

        RefreshPreflightReports();

        if (shouldCopyOnly)
        {
            Status = "Copied action to target.";
            AppLog.Info($"Copied action left->right sourcePage={sourcePageId} targetPage={targetPageId} coordinate={coordinate}");
        }
        else
        {
            Status = $"Moved action from {(drag.SourceSide == PaneSide.Left ? "source" : "target")} to {(side == PaneSide.Left ? "source" : "target")}.";
            AppLog.Info($"Moved action cross-pane from={drag.SourceSide} to={side} sourcePage={sourcePageId} targetPage={targetPageId} coordinate={coordinate}");
        }

        DragContext = null;
    }
[RelayCommand]
    public async Task SaveProfile(PaneSide side)
    {
        var profile = GetProfile(side);
        if (profile is null) return;

        var picker = new FileSavePicker();
        WindowHelper.InitializePicker(picker);
        picker.FileTypeChoices.Add("Stream Deck Profile", [".streamDeckProfile"]);
        picker.SuggestedFileName = $"{profile.DisplayName}-converted.streamDeckProfile";

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            Status = "Save canceled.";
            AppLog.Info($"Save canceled side={side}");
            return;
        }

        try
        {
            _archiveService.SaveProfile(profile, file.Path);
            var report = _archiveService.GetPreflightReport(profile);
            Status = report.ErrorCount > 0
                ? $"Saved {file.Name} with {report.ErrorCount} preflight error(s)."
                : $"Saved {file.Name}.";
            RefreshPreflight(side);
            AppLog.Info($"Saved profile side={side} file={file.Name}");
        }
        catch (Exception ex)
        {
            Status = $"Failed to save profile: {ex.Message}";
            AppLog.Error($"Failed saving profile side={side} error={ex.Message}");
        }
    }

    [RelayCommand]
    public void OpenTargetInStreamDeck()
    {
        if (RightProfile is null)
        {
            Status = "No target profile loaded.";
            return;
        }

        try
        {
            var tempPath = TemporaryOpenPath(RightProfile);
            _archiveService.SaveProfile(RightProfile, tempPath);

            if (TryOpenInStreamDeck(tempPath))
            {
                Status = "Opened target profile in Stream Deck.";
                AppLog.Info("Opened target profile in Stream Deck app.");
                return;
            }

            Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true });
            Status = "Opened target profile.";
            AppLog.Info("Opened target profile via shell association fallback.");
        }
        catch (Exception ex)
        {
            Status = $"Failed to open profile: {ex.Message}";
            AppLog.Error($"Failed opening target in Stream Deck error={ex.Message}");
        }
    }

    [RelayCommand]
    public void OpenLogsFolder()
    {
        var logsDir = AppLog.LogsDirectoryPath();
        if (logsDir is null)
        {
            Status = "Logs folder is unavailable.";
            return;
        }

        try
        {
            Directory.CreateDirectory(logsDir);
            Process.Start(new ProcessStartInfo(logsDir) { UseShellExecute = true });
            Status = "Opened logs folder.";
            AppLog.Info($"Opened logs folder path={logsDir}");
        }
        catch (Exception ex)
        {
            Status = $"Failed to open logs folder: {ex.Message}";
            AppLog.Error($"Failed to open logs folder error={ex.Message}");
        }
    }

    [RelayCommand]
    public void CheckForUpdates()
    {
        try
        {
            Process.Start(new ProcessStartInfo(UpdateUrl) { UseShellExecute = true });
            Status = "Opened update page.";
            AppLog.Info($"Opened update URL {UpdateUrl}");
        }
        catch (Exception ex)
        {
            Status = $"Failed to open update page: {ex.Message}";
            AppLog.Error($"Failed opening update URL {UpdateUrl} error={ex.Message}");
        }
    }

    [RelayCommand]
    public void OpenDiagnostics()
    {
        var logsDir = AppLog.LogsDirectoryPath() ?? Path.GetTempPath();
        try
        {
            Directory.CreateDirectory(logsDir);

            var path = Path.Combine(logsDir, $"diagnostics-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt");
            var lines = new[]
            {
                "SD Profile Manager Diagnostics",
                $"GeneratedUtc: {DateTime.UtcNow:O}",
                $"AppVersion: {typeof(WorkspaceViewModel).Assembly.GetName().Version}",
                $"Runtime: {Environment.Version}",
                $"OS: {Environment.OSVersion}",
                $"Machine: {Environment.MachineName}",
                $"LeftProfileLoaded: {LeftProfile is not null}",
                $"RightProfileLoaded: {RightProfile is not null}",
                $"CanUndo: {CanUndo}",
                $"CanRedo: {CanRedo}",
                $"LogsDir: {logsDir}"
            };

            File.WriteAllLines(path, lines);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            Status = "Opened diagnostics report.";
            AppLog.Info($"Opened diagnostics report path={path}");
        }
        catch (Exception ex)
        {
            Status = $"Failed to create diagnostics report: {ex.Message}";
            AppLog.Error($"Failed diagnostics report error={ex.Message}");
        }
    }

    // --- Internal helpers ---

    public ProfileArchive? GetProfile(PaneSide side) => side == PaneSide.Left ? LeftProfile : RightProfile;

    public string GetViewPageId(PaneSide side)
    {
        EnsurePaneViewPage(side, updateProfileActivePage: false);
        return side == PaneSide.Left ? LeftViewPageId : RightViewPageId;
    }

    public void EnableSingleProfileMode(PaneSide anchorSide)
    {
        var anchorProfile = GetProfile(anchorSide);
        if (anchorProfile is null)
            return;

        var anchorPageId = ResolvePanePageId(anchorProfile, GetViewPageId(anchorSide));
        var otherSide = OppositeSide(anchorSide);
        var otherPageId = anchorProfile.AllPageIds
            .FirstOrDefault(id => !string.Equals(id, anchorPageId, StringComparison.OrdinalIgnoreCase))
            ?? anchorPageId;

        SetProfileForPane(anchorSide, anchorProfile, anchorPageId);
        SetProfileForPane(otherSide, anchorProfile, otherPageId);
        LayoutMode = WorkspaceLayoutMode.SingleProfile;

        SetPaneViewPage(anchorSide, anchorPageId, updateProfileActivePage: true);
        SetPaneViewPage(otherSide, otherPageId, updateProfileActivePage: false);

        ResetFolderNavigation(PaneSide.Left);
        ResetFolderNavigation(PaneSide.Right);
        DragContext = null;
        OnPropertyChanged(nameof(IsSharedProfileView));
        RefreshPreflightReports();
    }

    public void DisableSingleProfileMode(PaneSide keepSide, bool cloneSharedProfile = true, bool updateStatus = true)
    {
        if (cloneSharedProfile && IsSharedProfileView && LeftProfile is not null)
        {
            var shared = LeftProfile;
            var sharedSnapshot = shared.Snapshot();
            var clone = ProfileArchive.Restore(sharedSnapshot);
            var keepPageId = GetViewPageId(keepSide);
            var otherSide = OppositeSide(keepSide);
            var otherPageId = GetViewPageId(otherSide);

            if (keepSide == PaneSide.Left)
            {
                SetProfileForPane(PaneSide.Left, shared, keepPageId);
                SetProfileForPane(PaneSide.Right, clone, otherPageId);
            }
            else
            {
                SetProfileForPane(PaneSide.Right, shared, keepPageId);
                SetProfileForPane(PaneSide.Left, clone, otherPageId);
            }
        }

        LayoutMode = WorkspaceLayoutMode.DualProfile;
        EnsurePaneViewPage(PaneSide.Left, updateProfileActivePage: false);
        EnsurePaneViewPage(PaneSide.Right, updateProfileActivePage: false);
        var leftPageId = GetViewPageId(PaneSide.Left);
        var rightPageId = GetViewPageId(PaneSide.Right);
        if (!string.IsNullOrWhiteSpace(leftPageId))
            SetPaneViewPage(PaneSide.Left, leftPageId, updateProfileActivePage: true);
        if (!string.IsNullOrWhiteSpace(rightPageId))
            SetPaneViewPage(PaneSide.Right, rightPageId, updateProfileActivePage: true);
        ResetFolderNavigation(PaneSide.Left);
        ResetFolderNavigation(PaneSide.Right);
        DragContext = null;
        OnPropertyChanged(nameof(IsSharedProfileView));

        if (updateStatus)
            Status = "Single profile mode disabled.";
    }

    private void SetProfileForPane(PaneSide side, ProfileArchive? profile, string? preferredPageId = null)
    {
        DragContext = null;
        ResetFolderNavigation(side);

        if (side == PaneSide.Left)
        {
            LeftProfile = profile;
            LeftViewPageId = profile is null ? "" : ResolvePanePageId(profile, preferredPageId);
        }
        else
        {
            RightProfile = profile;
            RightViewPageId = profile is null ? "" : ResolvePanePageId(profile, preferredPageId);
        }
    }

    private void EnsureLayoutModeValidity()
    {
        if (_isApplyingHistory)
            return;

        if (LayoutMode == WorkspaceLayoutMode.SingleProfile && !IsSharedProfileView)
        {
            LayoutMode = WorkspaceLayoutMode.DualProfile;
            OnPropertyChanged(nameof(IsSharedProfileView));
        }
    }

    private void SetPaneViewPage(PaneSide side, string pageId, bool updateProfileActivePage)
    {
        var profile = GetProfile(side);
        if (profile is null)
            return;

        var resolved = ResolvePanePageId(profile, pageId);
        if (side == PaneSide.Left)
            LeftViewPageId = resolved;
        else
            RightViewPageId = resolved;

        if (updateProfileActivePage)
            profile.SetActivePage(resolved);
    }

    private void EnsurePaneViewPage(PaneSide side, bool updateProfileActivePage)
    {
        var profile = GetProfile(side);
        if (profile is null)
        {
            if (side == PaneSide.Left)
                LeftViewPageId = "";
            else
                RightViewPageId = "";
            return;
        }

        var current = side == PaneSide.Left ? LeftViewPageId : RightViewPageId;
        var resolved = ResolvePanePageId(profile, current);
        if (side == PaneSide.Left)
            LeftViewPageId = resolved;
        else
            RightViewPageId = resolved;

        if (updateProfileActivePage)
            profile.SetActivePage(resolved);
    }

    private static string ResolvePanePageId(ProfileArchive profile, string? preferredPageId)
    {
        var candidate = ProfileArchive.NormalizePageId(preferredPageId ?? "");
        if (!string.IsNullOrWhiteSpace(candidate) && profile.GetPageState(candidate) is not null)
            return candidate;

        var active = ProfileArchive.NormalizePageId(profile.ActivePageId);
        if (!string.IsNullOrWhiteSpace(active) && profile.GetPageState(active) is not null)
            return active;

        var first = profile.AllPageIds.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(first))
            return ProfileArchive.NormalizePageId(first);

        return ProfileArchive.NormalizePageId(profile.Preset.WorkingPageId);
    }

    private static PaneSide OppositeSide(PaneSide side) =>
        side == PaneSide.Left ? PaneSide.Right : PaneSide.Left;

    private List<string> GetFolderNavigationStack(PaneSide side) =>
        side == PaneSide.Left ? _leftFolderNavigation : _rightFolderNavigation;

    private void ResetFolderNavigation(PaneSide side)
    {
        var stack = GetFolderNavigationStack(side);
        if (stack.Count > 0)
            stack.Clear();
    }

    private void PruneFolderNavigation(PaneSide side)
    {
        var profile = GetProfile(side);
        var stack = GetFolderNavigationStack(side);
        if (stack.Count == 0)
            return;

        if (profile is null)
        {
            stack.Clear();
            return;
        }

        stack.RemoveAll(pageId => profile.GetPageState(pageId) is null);
    }

    private static bool IsVisibleTopLevelPage(ProfileArchive profile, string pageId)
    {
        var normalizedPageId = ProfileArchive.NormalizePageId(pageId);
        var visiblePageIds = profile.PageOrder.Count > 0
            ? profile.PageOrder
            : [profile.ActivePageId];

        return visiblePageIds.Any(id =>
            string.Equals(ProfileArchive.NormalizePageId(id), normalizedPageId, StringComparison.OrdinalIgnoreCase));
    }

    private static string TemporaryOpenPath(ProfileArchive profile)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "SDProfileManager-Open");
        Directory.CreateDirectory(tempDir);
        var safeName = profile.DisplayName.Replace("/", "-").Replace("\\", "-");
        return Path.Combine(tempDir, $"{safeName}-{Guid.NewGuid():N}.streamDeckProfile");
    }

    private static bool TryOpenInStreamDeck(string filePath)
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Elgato", "StreamDeck", "StreamDeck.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Elgato", "StreamDeck", "StreamDeck.exe")
        };

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = candidate,
                    UseShellExecute = true,
                    Arguments = $"\"{filePath}\""
                });
                return true;
            }
            catch
            {
                // Try next candidate and eventually fall back to shell association.
            }
        }

        return false;
    }

    private static void MergeRequiredPlugin(JsonNode action, ProfileArchive profile)
    {
        var plugin = action.GetProperty("Plugin")?.GetProperty("UUID").GetStringValue();
        if (string.IsNullOrEmpty(plugin)) return;

        var required = new HashSet<string>(profile.PackageManifest.RequiredPlugins ?? []);
        required.Add(plugin);
        profile.PackageManifest.RequiredPlugins = [.. required.Order()];
    }

    private static void PruneActions(ProfileTemplate template, ProfileArchive profile)
    {
        foreach (var pageId in profile.AllPageIds)
        {
            var keypad = profile.GetActions(ControllerKind.Keypad, pageId)
                .Where(kv =>
                {
                    var parts = kv.Key.Split(',');
                    return parts.Length == 2
                        && int.TryParse(parts[0], out var x) && int.TryParse(parts[1], out var y)
                        && x >= 0 && x < template.Columns && y >= 0 && y < template.Rows;
                }).ToDictionary(kv => kv.Key, kv => kv.Value);

            var encoder = profile.GetActions(ControllerKind.Encoder, pageId)
                .Where(kv =>
                {
                    var parts = kv.Key.Split(',');
                    return parts.Length == 2
                        && int.TryParse(parts[0], out var x)
                        && int.TryParse(parts[1], out var y)
                        && x >= 0 && x < template.Dials
                        && y >= 0 && y < template.GetEncoderRows();
                }).ToDictionary(kv => kv.Key, kv => kv.Value);

            profile.ReplaceActions(keypad, encoder, pageId);
        }
    }

    private WorkspaceHistorySnapshot CaptureHistorySnapshot()
    {
        var snapshot = new WorkspaceHistorySnapshot
        {
            LayoutMode = LayoutMode,
            SharedProfile = IsSharedProfileView,
            LeftViewPageId = LeftViewPageId,
            RightViewPageId = RightViewPageId,
            LockSourceProfile = LockSourceProfile
        };

        if (snapshot.SharedProfile)
        {
            snapshot.LeftProfile = LeftProfile?.Snapshot();
        }
        else
        {
            snapshot.LeftProfile = LeftProfile?.Snapshot();
            snapshot.RightProfile = RightProfile?.Snapshot();
        }

        return snapshot;
    }

    private void RecordHistorySnapshot()
    {
        if (_isApplyingHistory) return;
        _undoStack.Add(CaptureHistorySnapshot());
        if (_undoStack.Count > MaxHistoryDepth)
            _undoStack.RemoveRange(0, _undoStack.Count - MaxHistoryDepth);
        _redoStack.Clear();
        RefreshHistoryAvailability();
    }

    private void ApplyHistorySnapshot(WorkspaceHistorySnapshot snapshot)
    {
        _isApplyingHistory = true;
        try
        {
            if (snapshot.SharedProfile && snapshot.LeftProfile is not null)
            {
                var shared = ProfileArchive.Restore(snapshot.LeftProfile);
                LeftProfile = shared;
                RightProfile = shared;
                LayoutMode = WorkspaceLayoutMode.SingleProfile;
            }
            else
            {
                LeftProfile = snapshot.LeftProfile is not null ? ProfileArchive.Restore(snapshot.LeftProfile) : null;
                RightProfile = snapshot.RightProfile is not null ? ProfileArchive.Restore(snapshot.RightProfile) : null;
                LayoutMode = snapshot.LayoutMode;
            }

            LeftViewPageId = snapshot.LeftViewPageId;
            RightViewPageId = snapshot.RightViewPageId;
            EnsurePaneViewPage(PaneSide.Left, updateProfileActivePage: false);
            EnsurePaneViewPage(PaneSide.Right, updateProfileActivePage: false);

            if (LayoutMode == WorkspaceLayoutMode.SingleProfile && !IsSharedProfileView)
                LayoutMode = WorkspaceLayoutMode.DualProfile;

            if (LeftProfile is not null)
                LeftProfile.SetActivePage(GetViewPageId(PaneSide.Left));
            if (RightProfile is not null && !ReferenceEquals(LeftProfile, RightProfile))
                RightProfile.SetActivePage(GetViewPageId(PaneSide.Right));

            LockSourceProfile = snapshot.LockSourceProfile;
            ResetFolderNavigation(PaneSide.Left);
            ResetFolderNavigation(PaneSide.Right);
            DragContext = null;
            OnPropertyChanged(nameof(IsSharedProfileView));
            RefreshPreflightReports();
        }
        finally
        {
            _isApplyingHistory = false;
        }
    }

    private void RefreshHistoryAvailability()
    {
        CanUndo = _undoStack.Count > 0;
        CanRedo = _redoStack.Count > 0;
    }

    private void RefreshPreflight(PaneSide side)
    {
        var profile = GetProfile(side);
        if (profile is null)
        {
            if (side == PaneSide.Left) LeftPreflightReport = null;
            else RightPreflightReport = null;
            return;
        }

        var report = _archiveService.GetPreflightReport(profile);
        if (side == PaneSide.Left) LeftPreflightReport = report;
        else RightPreflightReport = report;
    }

    private void RefreshPreflightReports()
    {
        RefreshPreflight(PaneSide.Left);
        RefreshPreflight(PaneSide.Right);
    }

    private class WorkspaceHistorySnapshot
    {
        public ProfileArchiveSnapshot? LeftProfile { get; set; }
        public ProfileArchiveSnapshot? RightProfile { get; set; }
        public WorkspaceLayoutMode LayoutMode { get; set; } = WorkspaceLayoutMode.DualProfile;
        public bool SharedProfile { get; set; }
        public string LeftViewPageId { get; set; } = "";
        public string RightViewPageId { get; set; } = "";
        public bool LockSourceProfile { get; set; }
    }
}
