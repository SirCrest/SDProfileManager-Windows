using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;
using SDProfileManager.Helpers;

namespace SDProfileManager.Models;

public partial class ProfileArchive : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();
    public string? SourcePath { get; }
    public string ExtractedRootPath { get; }

    [ObservableProperty] private ProfileTemplate _preset;
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _profileRootName;
    [ObservableProperty] private string _activePageId;
    [ObservableProperty] private List<string> _pageOrder;
    [ObservableProperty] private Dictionary<string, ProfilePageState> _pageStates;
    [ObservableProperty] private PackageManifest _packageManifest;
    [ObservableProperty] private RootProfileManifest _profileManifest;

    private readonly Dictionary<string, BitmapImage> _imageCache = [];

    public ProfileArchive(
        string? sourcePath,
        string extractedRootPath,
        ProfileTemplate preset,
        string name,
        string profileRootName,
        string activePageId,
        List<string> pageOrder,
        Dictionary<string, ProfilePageState> pageStates,
        PackageManifest packageManifest,
        RootProfileManifest profileManifest)
    {
        SourcePath = sourcePath;
        ExtractedRootPath = extractedRootPath;
        _preset = preset;
        _name = name;
        _profileRootName = profileRootName;
        _packageManifest = packageManifest;
        _profileManifest = profileManifest;

        // Normalize page states
        var normalizedStates = new Dictionary<string, ProfilePageState>();
        foreach (var (rawId, state) in pageStates)
        {
            var id = NormalizePageId(rawId);
            state.Id = id;
            normalizedStates[id] = state;
        }

        var normalizedOrder = UniquePageIds(pageOrder);
        var resolvedActive = NormalizePageId(activePageId);

        if (!normalizedStates.ContainsKey(resolvedActive))
        {
            var first = normalizedOrder.FirstOrDefault(id => normalizedStates.ContainsKey(id));
            if (first is not null)
            {
                resolvedActive = first;
            }
            else
            {
                first = normalizedStates.Keys.Order().FirstOrDefault();
                if (first is not null)
                {
                    resolvedActive = first;
                }
                else
                {
                    var fallbackId = NormalizePageId(preset.WorkingPageId);
                    resolvedActive = fallbackId;
                    normalizedStates[fallbackId] = MakeEmptyPageState(fallbackId, preset);
                }
            }
        }

        var finalOrder = normalizedOrder.Where(id => normalizedStates.ContainsKey(id)).ToList();
        if (!finalOrder.Contains(resolvedActive))
            finalOrder.Add(resolvedActive);
        if (finalOrder.Count == 0)
            finalOrder = [resolvedActive];

        _activePageId = resolvedActive;
        _pageStates = normalizedStates;
        _pageOrder = finalOrder;
    }

    public string DisplayName
    {
        get
        {
            var trimmed = Name.Trim();
            if (!string.IsNullOrEmpty(trimmed)) return trimmed;
            if (SourcePath is not null)
                return Path.GetFileNameWithoutExtension(SourcePath);
            return "Untitled Profile";
        }
    }

    public string ProfileRootPath =>
        Path.Combine(ExtractedRootPath, "Profiles", ProfileRootName);

    public string PagesRootPath =>
        Path.Combine(ProfileRootPath, "Profiles");

    public List<string> AllPageIds
    {
        get
        {
            var ids = new List<string>(PageOrder);
            foreach (var id in PageStates.Keys.Order())
            {
                if (!ids.Contains(id))
                    ids.Add(id);
            }
            return ids;
        }
    }

    public Dictionary<string, JsonNode> KeypadActions
    {
        get => PageStates.TryGetValue(ActivePageId, out var state) ? state.KeypadActions : [];
        set => UpsertPageState(ActivePageId, s => s.KeypadActions = value);
    }

    public Dictionary<string, JsonNode> EncoderActions
    {
        get => PageStates.TryGetValue(ActivePageId, out var state) ? state.EncoderActions : [];
        set => UpsertPageState(ActivePageId, s => s.EncoderActions = value);
    }

    public ProfilePageState? GetPageState(string pageId) =>
        PageStates.TryGetValue(NormalizePageId(pageId), out var state) ? state : null;

    public List<ProfilePageState> AllPageStatesInOrder() =>
        AllPageIds.Select(id => PageStates.TryGetValue(id, out var s) ? s : null).Where(s => s is not null).ToList()!;

    public void SetActivePage(string pageId)
    {
        var normalized = NormalizePageId(pageId);
        if (!PageStates.ContainsKey(normalized)) return;
        ActivePageId = normalized;
    }

    public string CreatePage()
    {
        string newId;
        do { newId = Guid.NewGuid().ToString().ToLowerInvariant(); }
        while (PageStates.ContainsKey(newId));

        PageStates[newId] = MakeEmptyPageState(newId, Preset);
        if (!PageOrder.Contains(newId))
        {
            PageOrder = [.. PageOrder, newId];
        }
        ActivePageId = newId;
        return newId;
    }

    public bool RemovePage(string pageId)
    {
        var normalized = NormalizePageId(pageId);
        if (!PageStates.ContainsKey(normalized))
            return false;

        var visiblePageIds = PageOrder.Count > 0
            ? UniquePageIds(PageOrder)
            : [NormalizePageId(ActivePageId)];

        if (visiblePageIds.Count <= 1)
            return false;

        var nextStates = new Dictionary<string, ProfilePageState>(PageStates);
        nextStates.Remove(normalized);
        PageStates = nextStates;

        var nextOrder = PageOrder
            .Select(NormalizePageId)
            .Where(id => !string.Equals(id, normalized, StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToList();

        if (nextOrder.Count == 0)
            nextOrder = [.. PageStates.Keys.Order()];

        PageOrder = nextOrder;

        if (string.Equals(ActivePageId, normalized, StringComparison.OrdinalIgnoreCase))
        {
            ActivePageId = nextOrder.FirstOrDefault()
                ?? PageStates.Keys.Order().FirstOrDefault()
                ?? NormalizePageId(Preset.WorkingPageId);
        }

        return true;
    }

    public string PageFolderName(string pageId) =>
        NormalizePageId(pageId).ToUpperInvariant();

    public string? ExistingPageDirectoryPath(string pageId)
    {
        if (!Directory.Exists(PagesRootPath)) return null;
        return Directory.EnumerateDirectories(PagesRootPath)
            .FirstOrDefault(d => string.Equals(Path.GetFileName(d), pageId, StringComparison.OrdinalIgnoreCase));
    }

    public string PageDirectoryPath(string pageId, bool preferExisting = true)
    {
        if (preferExisting)
        {
            var existing = ExistingPageDirectoryPath(pageId);
            if (existing is not null) return existing;
        }
        return Path.Combine(PagesRootPath, PageFolderName(pageId));
    }

    public string ActivePageDirectoryPath(bool preferExisting = true) =>
        PageDirectoryPath(ActivePageId, preferExisting);

    public JsonNode? GetAction(ControllerKind controller, string coordinate, string? pageId = null)
    {
        var resolvedPageId = NormalizePageId(pageId ?? ActivePageId);
        if (!PageStates.TryGetValue(resolvedPageId, out var state)) return null;

        return controller switch
        {
            ControllerKind.Keypad => state.KeypadActions.TryGetValue(coordinate, out var a) ? a : null,
            ControllerKind.Encoder => state.EncoderActions.TryGetValue(coordinate, out var a) ? a : null,
            _ => null
        };
    }

    public void SetAction(JsonNode? action, ControllerKind controller, string coordinate, string? pageId = null)
    {
        var resolvedPageId = NormalizePageId(pageId ?? ActivePageId);
        UpsertPageState(resolvedPageId, state =>
        {
            var dict = controller switch
            {
                ControllerKind.Keypad => state.KeypadActions,
                ControllerKind.Encoder => state.EncoderActions,
                _ => null
            };

            if (dict is null) return;

            if (action is not null)
                dict[coordinate] = action;
            else
                dict.Remove(coordinate);

            state.Manifest.Controllers = ControllersFromState(state, Preset);
        });
    }

    public void RemoveAction(ControllerKind controller, string coordinate, string? pageId = null) =>
        SetAction(null, controller, coordinate, pageId);

    public void ReplaceActions(Dictionary<string, JsonNode> keypad, Dictionary<string, JsonNode> encoder, string pageId)
    {
        var resolvedPageId = NormalizePageId(pageId);
        UpsertPageState(resolvedPageId, state =>
        {
            state.KeypadActions = keypad;
            state.EncoderActions = encoder;
            state.Manifest.Controllers = ControllersFromState(state, Preset);
        });
    }

    public Dictionary<string, JsonNode> GetActions(ControllerKind kind, string pageId)
    {
        if (!PageStates.TryGetValue(NormalizePageId(pageId), out var state))
            return [];
        return kind switch
        {
            ControllerKind.Keypad => state.KeypadActions,
            ControllerKind.Encoder => state.EncoderActions,
            _ => []
        };
    }

    public List<JsonNode> AllActions()
    {
        var actions = new List<JsonNode>();
        foreach (var state in PageStates.Values)
        {
            actions.AddRange(state.KeypadActions.Values);
            actions.AddRange(state.EncoderActions.Values);
        }
        return actions;
    }

    public void UpdatePresetControllersForAllPages()
    {
        foreach (var (pageId, state) in PageStates)
        {
            state.Manifest.Controllers = ControllersFromState(state, Preset);
        }
        OnPropertyChanged(nameof(PageStates));
    }

    public void UpdatePageName(string value, string? pageId = null)
    {
        var resolvedPageId = NormalizePageId(pageId ?? ActivePageId);
        UpsertPageState(resolvedPageId, state =>
        {
            state.Manifest.Name = value;
        });
    }

    public ActionPresentation GetActionPresentation(JsonNode action)
    {
        var obj = action.GetObjectValue();
        if (obj is null)
            return new ActionPresentation();

        var pluginObj = obj["Plugin"]?.GetObjectValue();
        var pluginName = NormalizeLabel(
            pluginObj?["Name"].GetStringValue(),
            obj["Name"].GetStringValue(),
            "Action");
        var pluginUUID = pluginObj?["UUID"].GetStringValue();
        var stateIndex = obj["State"].GetIntValue() ?? 0;
        var states = obj["States"].GetArrayValue();

        JsonObject? stateObject = null;
        if (states is not null)
        {
            if (stateIndex >= 0 && stateIndex < states.Count)
                stateObject = states[stateIndex]?.GetObjectValue();
            else if (states.Count > 0)
                stateObject = states[0]?.GetObjectValue();
        }

        var stateTitle = NormalizeLabel(stateObject?["Title"].GetStringValue());
        var baseTitle = NormalizeLabel(obj["Name"].GetStringValue());
        var actionName = NormalizeLabel(stateTitle, baseTitle, pluginName, "Action");
        var displayName = actionName;
        if (!string.IsNullOrWhiteSpace(pluginName)
            && !string.Equals(pluginName, actionName, StringComparison.OrdinalIgnoreCase)
            && !actionName.Contains(pluginName, StringComparison.OrdinalIgnoreCase))
        {
            displayName = $"{actionName} - {pluginName}";
        }

        var stateImageRef = stateObject?["Image"].GetStringValue();
        var encoderImageRef = obj["Encoder"]?.GetObjectValue()?["Icon"].GetStringValue();
        var imageRef = !string.IsNullOrWhiteSpace(stateImageRef) ? stateImageRef : encoderImageRef;

        return new ActionPresentation
        {
            Title = actionName,
            ActionName = actionName,
            DisplayName = displayName,
            PluginName = pluginName,
            PluginUUID = pluginUUID,
            ImageReference = imageRef
        };
    }

    public string? ResolveImagePath(string reference, string? pageId = null)
    {
        var normalized = reference.Replace('\\', '/');
        var resolvedPageId = NormalizePageId(pageId ?? ActivePageId);

        var candidates = new List<(string basePath, string relative)>
        {
            (PageDirectoryPath(resolvedPageId), normalized),
            (ProfileRootPath, normalized),
            (ExtractedRootPath, normalized),
            (ProfileRootPath, $"Profiles/{PageFolderName(resolvedPageId)}/{normalized}"),
            (ProfileRootPath, $"Profiles/{resolvedPageId}/{normalized}")
        };

        foreach (var id in AllPageIds)
        {
            candidates.Add((ProfileRootPath, $"Profiles/{PageFolderName(id)}/{normalized}"));
            candidates.Add((ProfileRootPath, $"Profiles/{id}/{normalized}"));
        }

        foreach (var (basePath, relative) in candidates)
        {
            var resolved = FileHelper.ResolveCaseInsensitivePath(basePath, relative);
            if (resolved is not null && File.Exists(resolved))
                return resolved;
        }

        return null;
    }

    public void ClearImageCache() => _imageCache.Clear();

    public ProfileArchiveSnapshot Snapshot()
    {
        var snapshot = new ProfileArchiveSnapshot
        {
            SourcePath = SourcePath,
            ExtractedRootPath = ExtractedRootPath,
            PresetId = Preset.Id,
            Name = Name,
            ProfileRootName = ProfileRootName,
            ActivePageId = ActivePageId,
            PageOrder = [.. PageOrder],
            PackageManifestJson = JsonSerializer.Serialize(PackageManifest),
            ProfileManifestJson = JsonSerializer.Serialize(ProfileManifest)
        };

        foreach (var (pageId, state) in PageStates)
        {
            var pageSnapshot = new ProfilePageStateSnapshot
            {
                Id = state.Id,
                ManifestJson = JsonSerializer.Serialize(state.Manifest)
            };
            foreach (var (k, v) in state.KeypadActions)
                pageSnapshot.KeypadActionsJson[k] = v.ToJsonString();
            foreach (var (k, v) in state.EncoderActions)
                pageSnapshot.EncoderActionsJson[k] = v.ToJsonString();

            snapshot.PageStates[pageId] = pageSnapshot;
        }

        return snapshot;
    }

    public static ProfileArchive Restore(ProfileArchiveSnapshot snapshot)
    {
        var preset = ProfileTemplates.ById.TryGetValue(snapshot.PresetId, out var p)
            ? p
            : ProfileTemplates.GetTemplate(
                JsonSerializer.Deserialize<PackageManifest>(snapshot.PackageManifestJson)?.DeviceModel);

        var pageStates = new Dictionary<string, ProfilePageState>();
        foreach (var (pageId, ps) in snapshot.PageStates)
        {
            var state = new ProfilePageState
            {
                Id = ps.Id,
                Manifest = JsonSerializer.Deserialize<PageManifest>(ps.ManifestJson) ?? new()
            };
            foreach (var (k, v) in ps.KeypadActionsJson)
                state.KeypadActions[k] = JsonNode.Parse(v)!;
            foreach (var (k, v) in ps.EncoderActionsJson)
                state.EncoderActions[k] = JsonNode.Parse(v)!;
            pageStates[pageId] = state;
        }

        return new ProfileArchive(
            sourcePath: snapshot.SourcePath,
            extractedRootPath: snapshot.ExtractedRootPath,
            preset: preset,
            name: snapshot.Name,
            profileRootName: snapshot.ProfileRootName,
            activePageId: snapshot.ActivePageId,
            pageOrder: [.. snapshot.PageOrder],
            pageStates: pageStates,
            packageManifest: JsonSerializer.Deserialize<PackageManifest>(snapshot.PackageManifestJson) ?? new(),
            profileManifest: JsonSerializer.Deserialize<RootProfileManifest>(snapshot.ProfileManifestJson) ?? new()
        );
    }

    // --- Private helpers ---

    private void UpsertPageState(string pageId, Action<ProfilePageState> transform)
    {
        var id = NormalizePageId(pageId);
        if (!PageStates.TryGetValue(id, out var state))
        {
            state = MakeEmptyPageState(id, Preset);
            PageStates[id] = state;
            if (!PageOrder.Contains(id))
                PageOrder = [.. PageOrder, id];
        }
        transform(state);
        OnPropertyChanged(nameof(PageStates));
    }

    public static string NormalizePageId(string value) =>
        value.Trim().ToLowerInvariant();

    public static List<string> UniquePageIds(IEnumerable<string> values)
    {
        var seen = new HashSet<string>();
        var output = new List<string>();
        foreach (var value in values)
        {
            var normalized = NormalizePageId(value);
            if (string.IsNullOrEmpty(normalized) || !seen.Add(normalized))
                continue;
            output.Add(normalized);
        }
        return output;
    }

    private static string NormalizeLabel(params string?[] candidates)
    {
        foreach (var value in candidates)
        {
            var normalized = value?.Replace("\n", " ").Trim();
            if (!string.IsNullOrWhiteSpace(normalized))
                return normalized;
        }

        return string.Empty;
    }

    private static ProfilePageState MakeEmptyPageState(string id, ProfileTemplate preset)
    {
        var state = new ProfilePageState
        {
            Id = NormalizePageId(id),
            Manifest = new PageManifest()
        };
        state.Manifest.Controllers = ControllersFromState(state, preset);
        return state;
    }

    private static List<ControllerManifest> ControllersFromState(ProfilePageState state, ProfileTemplate preset)
    {
        return preset.ControllerOrder.Select(kind => new ControllerManifest
        {
            Type = kind.ToJsonString(),
            Actions = kind switch
            {
                ControllerKind.Keypad => new(state.KeypadActions),
                ControllerKind.Encoder => new(state.EncoderActions),
                _ => null
            }
        }).ToList();
    }
}
