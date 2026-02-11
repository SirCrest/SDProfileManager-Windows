using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using SDProfileManager.Helpers;
using SDProfileManager.Models;

namespace SDProfileManager.Services;

public class ArchiveServiceException : Exception
{
    public ArchiveServiceException(string message) : base(message) { }
}

public class ProfileArchiveService
{
    private static readonly JsonSerializerOptions _jsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions _jsonWriteOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public PreflightReport GetPreflightReport(ProfileArchive profile)
    {
        var issues = new List<PreflightIssue>();
        var seen = new HashSet<string>();
        const int maxIssueCount = 200;

        void AddIssue(PreflightSeverity severity, string code, string message)
        {
            if (issues.Count >= maxIssueCount) return;
            var key = $"{severity.ToCode()}|{code}|{message}";
            if (!seen.Add(key)) return;
            issues.Add(new PreflightIssue { Severity = severity, Code = code, Message = message });
        }

        var knownPageIds = new HashSet<string>(profile.AllPageIds.Select(ProfileArchive.NormalizePageId));

        if (!knownPageIds.Contains(ProfileArchive.NormalizePageId(profile.ActivePageId)))
            AddIssue(PreflightSeverity.Error, "ACTIVE_PAGE_MISSING", "Active page is missing from loaded page set.");

        var listedPages = profile.ProfileManifest.Pages?.Pages ?? [];
        foreach (var listed in listedPages)
        {
            var normalized = ProfileArchive.NormalizePageId(listed);
            if (string.IsNullOrEmpty(normalized)) continue;
            if (knownPageIds.Contains(normalized)) continue;
            if (profile.ExistingPageDirectoryPath(normalized) is null)
                AddIssue(PreflightSeverity.Warning, "PAGE_LISTED_MISSING", $"Manifest lists missing page {listed}.");
        }

        var defaultPageId = ProfileArchive.NormalizePageId(profile.ProfileManifest.Pages?.Default ?? "");
        if (!string.IsNullOrEmpty(defaultPageId) && !knownPageIds.Contains(defaultPageId)
            && profile.ExistingPageDirectoryPath(defaultPageId) is null)
            AddIssue(PreflightSeverity.Warning, "DEFAULT_PAGE_MISSING", $"Manifest default page is missing: {defaultPageId}.");

        var currentPageId = ProfileArchive.NormalizePageId(profile.ProfileManifest.Pages?.Current ?? "");
        if (!string.IsNullOrEmpty(currentPageId)
            && !string.Equals(currentPageId, ProfileArchive.NormalizePageId(ProfileTemplates.ZeroUuid), StringComparison.OrdinalIgnoreCase)
            && !knownPageIds.Contains(currentPageId)
            && profile.ExistingPageDirectoryPath(currentPageId) is null)
            AddIssue(PreflightSeverity.Warning, "CURRENT_PAGE_MISSING", $"Manifest current page is missing: {currentPageId}.");

        var requiredPlugins = new HashSet<string>(
            (profile.PackageManifest.RequiredPlugins ?? [])
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p)));
        var referencedPlugins = new HashSet<string>();

        foreach (var pageId in profile.AllPageIds)
        {
            var controllerActions = new (ControllerKind kind, Dictionary<string, JsonNode> actions)[]
            {
                (ControllerKind.Keypad, profile.GetActions(ControllerKind.Keypad, pageId)),
                (ControllerKind.Encoder, profile.GetActions(ControllerKind.Encoder, pageId))
            };

            foreach (var (controller, actions) in controllerActions)
            {
                foreach (var coordinate in actions.Keys.Order())
                {
                    if (!actions.TryGetValue(coordinate, out var action)) continue;
                    var slotLabel = $"{controller.ToJsonString()} {coordinate} on page {pageId}";

                    var pluginUuid = GetPluginUuid(action)?.Trim();
                    if (!string.IsNullOrEmpty(pluginUuid))
                        referencedPlugins.Add(pluginUuid);
                    else
                        AddIssue(PreflightSeverity.Warning, "PLUGIN_UUID_MISSING", $"Action at {slotLabel} does not include Plugin.UUID.");

                    var folderId = GetFolderProfileId(action)?.Trim();
                    if (!string.IsNullOrEmpty(folderId))
                    {
                        var normalizedFolderId = ProfileArchive.NormalizePageId(folderId);
                        if (!knownPageIds.Contains(normalizedFolderId) && profile.ExistingPageDirectoryPath(normalizedFolderId) is null)
                            AddIssue(PreflightSeverity.Error, "FOLDER_TARGET_MISSING", $"Folder action at {slotLabel} references missing page {folderId}.");
                    }

                    foreach (var imageRef in GetReferencedImagePaths(action).Order())
                    {
                        if (profile.ResolveImagePath(imageRef, pageId) is null)
                            AddIssue(PreflightSeverity.Error, "IMAGE_REF_MISSING", $"Action at {slotLabel} references missing image {imageRef}.");
                    }
                }
            }
        }

        foreach (var plugin in referencedPlugins.Order())
        {
            if (!requiredPlugins.Contains(plugin))
                AddIssue(PreflightSeverity.Warning, "REQUIRED_PLUGIN_MISSING", $"Plugin {plugin} is used but not listed in package RequiredPlugins.");
        }

        foreach (var plugin in requiredPlugins.Order())
        {
            if (!referencedPlugins.Contains(plugin))
                AddIssue(PreflightSeverity.Info, "REQUIRED_PLUGIN_UNUSED", $"RequiredPlugins contains {plugin} but no action currently references it.");
        }

        return new PreflightReport(issues);
    }

    public ProfileArchive LoadProfile(string archivePath)
    {
        AppLog.Info($"Loading profile archive={Path.GetFileName(archivePath)}");
        var workDir = MakeWorkingDirectory();

        ZipFile.ExtractToDirectory(archivePath, workDir);

        var packagePath = Path.Combine(workDir, "package.json");
        if (!File.Exists(packagePath))
            throw new ArchiveServiceException("Invalid archive: missing package.json");

        var packageManifest = JsonSerializer.Deserialize<PackageManifest>(File.ReadAllText(packagePath), _jsonReadOptions)
            ?? new PackageManifest();

        var profileRootName = FindProfileRootName(workDir);
        var profileRootPath = Path.Combine(workDir, "Profiles", profileRootName);
        var rootManifestPath = Path.Combine(profileRootPath, "manifest.json");
        var profileManifest = JsonSerializer.Deserialize<RootProfileManifest>(File.ReadAllText(rootManifestPath), _jsonReadOptions)
            ?? new RootProfileManifest();

        if (string.IsNullOrWhiteSpace(profileManifest.Name))
            profileManifest.Name = Path.GetFileNameWithoutExtension(archivePath);

        var deviceModel = packageManifest.DeviceModel ?? profileManifest.Device?.Model;
        var template = ProfileTemplates.GetTemplate(deviceModel);

        var pagesRootPath = Path.Combine(profileRootPath, "Profiles");
        var discoveredPageIds = DiscoverPageIds(pagesRootPath);
        var listedPageIds = OrderedUniquePageIds((profileManifest.Pages?.Pages ?? []).Select(NormalizePageId));
        var defaultPageId = NormalizePageId(profileManifest.Pages?.Default ?? template.DefaultPageId);

        var allPageIds = OrderedUniquePageIds(listedPageIds.Concat([defaultPageId]).Concat(discoveredPageIds));
        if (allPageIds.Count == 0)
            allPageIds = [NormalizePageId(template.WorkingPageId)];

        var pageStates = new Dictionary<string, ProfilePageState>();
        foreach (var pageId in allPageIds)
        {
            var page = LoadPageManifest(profileRootPath, pageId);
            if (page is null) continue;
            pageStates[pageId] = new ProfilePageState
            {
                Id = pageId,
                Manifest = page.Value.manifest,
                KeypadActions = GetActionsFromManifest(page.Value.manifest, ControllerKind.Keypad),
                EncoderActions = GetActionsFromManifest(page.Value.manifest, ControllerKind.Encoder)
            };
        }

        if (pageStates.Count == 0)
            throw new ArchiveServiceException("No valid page manifests found in profile.");

        var pageOrder = listedPageIds.Where(id => pageStates.ContainsKey(id)).ToList();
        if (pageOrder.Count == 0)
        {
            var sortedDiscovered = discoveredPageIds.Where(id => pageStates.ContainsKey(id)).ToList();
            var firstAction = sortedDiscovered.FirstOrDefault(id => HasAnyActions(pageStates.TryGetValue(id, out var s) ? s : null));
            if (firstAction is not null)
                pageOrder = [firstAction];
            else if (pageStates.ContainsKey(defaultPageId))
                pageOrder = [defaultPageId];
            else if (sortedDiscovered.Count > 0)
                pageOrder = [sortedDiscovered[0]];
        }

        foreach (var pageId in discoveredPageIds)
        {
            if (pageId == defaultPageId) continue;
            if (!pageStates.ContainsKey(pageId)) continue;
            if (!pageOrder.Contains(pageId))
                pageOrder.Add(pageId);
        }

        if (pageOrder.Count == 0)
            pageOrder = [.. pageStates.Keys.Order()];

        var activePageId = SelectActivePageId(profileManifest, pageOrder, pageStates, template.WorkingPageId);
        packageManifest.DeviceModel = template.DeviceModel;

        var archive = new ProfileArchive(
            sourcePath: archivePath,
            extractedRootPath: workDir,
            preset: template,
            name: profileManifest.Name ?? Path.GetFileNameWithoutExtension(archivePath),
            profileRootName: profileRootName,
            activePageId: activePageId,
            pageOrder: pageOrder,
            pageStates: pageStates,
            packageManifest: packageManifest,
            profileManifest: profileManifest
        );

        AppLog.Info($"Loaded profile name={archive.DisplayName} pages={archive.PageOrder.Count} device={template.DeviceModel}");
        return archive;
    }

    public ProfileArchive CreateEmptyProfile(ProfileTemplate template, string name = "Untitled Profile")
    {
        AppLog.Info($"Creating empty profile template={template.Id} name={name}");
        var workDir = MakeWorkingDirectory();
        var profileRootPath = Path.Combine(workDir, "Profiles", template.ProfileRootName);
        var pagesRootPath = Path.Combine(profileRootPath, "Profiles");

        Directory.CreateDirectory(pagesRootPath);
        Directory.CreateDirectory(Path.Combine(profileRootPath, "Images"));

        var defaultPagePath = Path.Combine(pagesRootPath, template.DefaultPageId.ToUpperInvariant());
        var workingPagePath = Path.Combine(pagesRootPath, template.WorkingPageId.ToUpperInvariant());

        Directory.CreateDirectory(Path.Combine(defaultPagePath, "Images"));
        Directory.CreateDirectory(Path.Combine(workingPagePath, "Images"));

        var packageManifest = new PackageManifest
        {
            AppVersion = "7.3.0.22513",
            DeviceModel = template.DeviceModel,
            DeviceSettings = null,
            FormatVersion = 1,
            OSType = "Windows",
            OSVersion = Environment.OSVersion.Version.ToString(),
            RequiredPlugins = []
        };

        var profileManifest = new RootProfileManifest
        {
            Device = new DeviceManifest { Model = template.DeviceModel, UUID = Guid.NewGuid().ToString().ToLowerInvariant() },
            Name = name,
            Pages = new PagesManifest
            {
                Current = ProfileTemplates.ZeroUuid,
                Default = template.DefaultPageId,
                Pages = [template.WorkingPageId]
            },
            Version = "3.0"
        };

        var defaultState = BlankPageState(template.DefaultPageId, template);
        var workingState = BlankPageState(template.WorkingPageId, template);

        WriteJson(packageManifest, Path.Combine(workDir, "package.json"));
        WriteJson(profileManifest, Path.Combine(profileRootPath, "manifest.json"));
        WriteJson(defaultState.Manifest, Path.Combine(defaultPagePath, "manifest.json"));
        WriteJson(workingState.Manifest, Path.Combine(workingPagePath, "manifest.json"));

        return new ProfileArchive(
            sourcePath: null,
            extractedRootPath: workDir,
            preset: template,
            name: name,
            profileRootName: template.ProfileRootName,
            activePageId: template.WorkingPageId,
            pageOrder: [NormalizePageId(template.WorkingPageId)],
            pageStates: new Dictionary<string, ProfilePageState>
            {
                [NormalizePageId(template.WorkingPageId)] = workingState,
                [NormalizePageId(template.DefaultPageId)] = defaultState
            },
            packageManifest: packageManifest,
            profileManifest: profileManifest
        );
    }

    public void SaveProfile(ProfileArchive profile, string outputPath)
    {
        AppLog.Info($"Saving profile name={profile.DisplayName} pages={profile.PageOrder.Count} output={Path.GetFileName(outputPath)}");
        var template = profile.Preset;
        var stagingDir = MakeWorkingDirectory("stage");
        FileHelper.CopyDirectoryRecursive(profile.ExtractedRootPath, stagingDir);

        var profilesRootPath = Path.Combine(stagingDir, "Profiles");
        var sourceRootPath = Path.Combine(profilesRootPath, profile.ProfileRootName);
        var targetRootPath = Path.Combine(profilesRootPath, template.ProfileRootName);

        if (!string.Equals(sourceRootPath, targetRootPath, StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.Exists(targetRootPath))
                Directory.Delete(targetRootPath, recursive: true);
            Directory.Move(sourceRootPath, targetRootPath);
            profile.ProfileRootName = template.ProfileRootName;
        }

        var pagesRootPath = Path.Combine(targetRootPath, "Profiles");
        Directory.CreateDirectory(pagesRootPath);
        Directory.CreateDirectory(Path.Combine(targetRootPath, "Images"));

        var exportPageIds = GetExportPageIds(profile);
        var defaultPageId = NormalizePageId(profile.ProfileManifest.Pages?.Default ?? template.DefaultPageId);

        var folderIds = GetReferencedFolderIds(profile);
        var allowedPageIds = new HashSet<string>(exportPageIds.Concat([defaultPageId]).Concat(folderIds));

        CanonicalizePageFolders(pagesRootPath, allowedPageIds);

        var defaultPageState = BlankPageState(defaultPageId, template);
        var defaultPagePath = Path.Combine(pagesRootPath, defaultPageId.ToUpperInvariant());
        Directory.CreateDirectory(Path.Combine(defaultPagePath, "Images"));
        WriteJson(defaultPageState.Manifest, Path.Combine(defaultPagePath, "manifest.json"));

        foreach (var pageId in exportPageIds)
        {
            var pagePath = Path.Combine(pagesRootPath, pageId.ToUpperInvariant());
            Directory.CreateDirectory(Path.Combine(pagePath, "Images"));

            var state = profile.GetPageState(pageId);
            if (state is null)
            {
                var fallback = BlankPageState(pageId, template);
                WriteJson(fallback.Manifest, Path.Combine(pagePath, "manifest.json"));
                continue;
            }

            var pageManifest = state.Manifest;
            pageManifest.Controllers = ControllersForState(state, template);
            WriteJson(pageManifest, Path.Combine(pagePath, "manifest.json"));
        }

        profile.ProfileManifest.Name = profile.DisplayName;
        profile.ProfileManifest.Device = new DeviceManifest
        {
            Model = template.DeviceModel,
            UUID = profile.ProfileManifest.Device?.UUID ?? Guid.NewGuid().ToString().ToLowerInvariant()
        };
        profile.ProfileManifest.Pages = new PagesManifest
        {
            Current = ProfileTemplates.ZeroUuid,
            Default = defaultPageId,
            Pages = exportPageIds
        };
        profile.ProfileManifest.Version ??= "3.0";

        var requiredPlugins = new HashSet<string>(profile.PackageManifest.RequiredPlugins ?? []);
        foreach (var action in profile.AllActions())
        {
            var plugin = GetPluginUuid(action);
            if (!string.IsNullOrEmpty(plugin))
                requiredPlugins.Add(plugin);
        }

        profile.PackageManifest.DeviceModel = template.DeviceModel;
        profile.PackageManifest.RequiredPlugins = [.. requiredPlugins.Order()];
        profile.PackageManifest.FormatVersion ??= 1;
        profile.PackageManifest.AppVersion ??= "7.3.0.22513";
        profile.PackageManifest.OSType ??= "Windows";
        profile.PackageManifest.OSVersion ??= Environment.OSVersion.Version.ToString();

        WriteJson(profile.PackageManifest, Path.Combine(stagingDir, "package.json"));
        WriteJson(profile.ProfileManifest, Path.Combine(targetRootPath, "manifest.json"));

        if (File.Exists(outputPath))
            File.Delete(outputPath);

        ZipFile.CreateFromDirectory(stagingDir, outputPath);
        AppLog.Info($"Saved profile archive file={Path.GetFileName(outputPath)}");
    }

    public void CopyReferencedFiles(JsonNode action, ProfileArchive source, ProfileArchive target,
        string sourcePageId, string targetPageId)
    {
        var normalizedSourcePageId = ProfileArchive.NormalizePageId(sourcePageId);
        var normalizedTargetPageId = ProfileArchive.NormalizePageId(targetPageId);

        foreach (var reference in GetReferencedImagePaths(action))
        {
            var sourcePath = source.ResolveImagePath(reference, normalizedSourcePageId);
            if (sourcePath is null) continue;

            var normalized = reference.Replace('\\', '/');
            string destinationPath;

            if (normalized.StartsWith("Images/", StringComparison.OrdinalIgnoreCase))
            {
                destinationPath = Path.Combine(target.PageDirectoryPath(normalizedTargetPageId), normalized.Replace('/', Path.DirectorySeparatorChar));
            }
            else if (normalized.StartsWith("Profiles/", StringComparison.OrdinalIgnoreCase))
            {
                var parts = normalized.Split('/');
                if (parts.Length >= 3)
                {
                    var sourceFolder = parts[1];
                    var mappedFolder = string.Equals(sourceFolder, normalizedSourcePageId, StringComparison.OrdinalIgnoreCase)
                        ? target.PageFolderName(normalizedTargetPageId)
                        : sourceFolder.ToUpperInvariant();
                    var remainder = string.Join(Path.DirectorySeparatorChar.ToString(), parts.Skip(2));
                    destinationPath = Path.Combine(target.PagesRootPath, mappedFolder, remainder);
                }
                else
                {
                    destinationPath = Path.Combine(target.ProfileRootPath, normalized.Replace('/', Path.DirectorySeparatorChar));
                }
            }
            else
            {
                destinationPath = Path.Combine(target.PageDirectoryPath(normalizedTargetPageId), "Images", Path.GetFileName(sourcePath));
            }

            if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var destDir = Path.GetDirectoryName(destinationPath);
                if (destDir is not null) Directory.CreateDirectory(destDir);
                File.Copy(sourcePath, destinationPath, overwrite: true);
            }
            catch { /* Silently ignore file copy failures */ }
        }

        var folderId = GetFolderProfileId(action);
        if (string.IsNullOrEmpty(folderId)) return;

        var sourceFolderPath = source.PageDirectoryPath(folderId, preferExisting: true);
        var targetFolderId = ProfileArchive.NormalizePageId(folderId);
        if (ReferenceEquals(source, target) && string.Equals(ProfileArchive.NormalizePageId(folderId), normalizedSourcePageId, StringComparison.OrdinalIgnoreCase))
            targetFolderId = normalizedTargetPageId;

        var targetFolderPath = Path.Combine(target.PagesRootPath, target.PageFolderName(targetFolderId));

        if (string.Equals(Path.GetFullPath(sourceFolderPath), Path.GetFullPath(targetFolderPath), StringComparison.OrdinalIgnoreCase))
            return;

        if (Directory.Exists(sourceFolderPath))
        {
            try { FileHelper.MergeDirectory(sourceFolderPath, targetFolderPath); }
            catch { /* Silently ignore merge failures */ }
        }
    }

    // --- Private helpers ---

    private static string FindProfileRootName(string rootPath)
    {
        var profilesRoot = Path.Combine(rootPath, "Profiles");
        if (!Directory.Exists(profilesRoot))
            throw new ArchiveServiceException("Invalid archive: missing Profiles directory.");

        var root = Directory.EnumerateDirectories(profilesRoot)
            .FirstOrDefault(d => Path.GetFileName(d).EndsWith(".sdProfile", StringComparison.OrdinalIgnoreCase));

        if (root is null)
            throw new ArchiveServiceException("Missing profile root (.sdProfile folder).");

        return Path.GetFileName(root);
    }

    private (PageManifest manifest, string folder)? LoadPageManifest(string profileRootPath, string pageId)
    {
        var pagesRoot = Path.Combine(profileRootPath, "Profiles");
        var pageFolder = ExistingPageFolder(pagesRoot, pageId);
        if (pageFolder is null) return null;

        var manifestPath = Path.Combine(pageFolder, "manifest.json");
        if (!File.Exists(manifestPath)) return null;

        var manifest = JsonSerializer.Deserialize<PageManifest>(File.ReadAllText(manifestPath), _jsonReadOptions);
        if (manifest is null) return null;

        return (manifest, Path.GetFileName(pageFolder));
    }

    private static string? ExistingPageFolder(string pagesRoot, string pageId)
    {
        if (!Directory.Exists(pagesRoot)) return null;

        var upper = Path.Combine(pagesRoot, pageId.ToUpperInvariant());
        if (Directory.Exists(upper)) return upper;

        var lower = Path.Combine(pagesRoot, pageId.ToLowerInvariant());
        if (Directory.Exists(lower)) return lower;

        return Directory.EnumerateDirectories(pagesRoot)
            .FirstOrDefault(d => string.Equals(Path.GetFileName(d), pageId, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> DiscoverPageIds(string pagesRoot)
    {
        if (!Directory.Exists(pagesRoot)) return [];

        var ids = new List<string>();
        foreach (var dir in Directory.EnumerateDirectories(pagesRoot))
        {
            var manifestPath = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestPath)) continue;
            ids.Add(NormalizePageId(Path.GetFileName(dir)));
        }
        return OrderedUniquePageIds(ids);
    }

    private static bool HasAnyActions(ProfilePageState? state)
    {
        if (state is null) return false;
        return state.KeypadActions.Count > 0 || state.EncoderActions.Count > 0;
    }

    private static string SelectActivePageId(RootProfileManifest profileManifest, List<string> pageOrder,
        Dictionary<string, ProfilePageState> pageStates, string fallbackPageId)
    {
        var current = NormalizePageId(profileManifest.Pages?.Current ?? "");
        if (!string.IsNullOrEmpty(current) && !string.Equals(current, NormalizePageId(ProfileTemplates.ZeroUuid), StringComparison.OrdinalIgnoreCase)
            && pageStates.ContainsKey(current))
            return current;

        var candidates = OrderedUniquePageIds(
            pageOrder.Concat((profileManifest.Pages?.Pages ?? []).Select(NormalizePageId)));

        var firstWithActions = candidates.FirstOrDefault(id => HasAnyActions(pageStates.TryGetValue(id, out var s) ? s : null));
        if (firstWithActions is not null) return firstWithActions;

        var firstExisting = candidates.FirstOrDefault(id => pageStates.ContainsKey(id));
        if (firstExisting is not null) return firstExisting;

        var defaultId = NormalizePageId(profileManifest.Pages?.Default ?? "");
        if (!string.IsNullOrEmpty(defaultId) && pageStates.ContainsKey(defaultId))
            return defaultId;

        var fallbackId = NormalizePageId(fallbackPageId);
        if (pageStates.ContainsKey(fallbackId))
            return fallbackId;

        return pageStates.Keys.Order().FirstOrDefault() ?? fallbackId;
    }

    private static Dictionary<string, JsonNode> GetActionsFromManifest(PageManifest page, ControllerKind kind)
    {
        var controller = page.Controllers.FirstOrDefault(c => c.Type == kind.ToJsonString());
        if (controller?.Actions is null) return [];
        return new Dictionary<string, JsonNode>(controller.Actions);
    }

    private static List<ControllerManifest> ControllersForState(ProfilePageState state, ProfileTemplate template)
    {
        return template.ControllerOrder.Select(kind => new ControllerManifest
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

    private static ProfilePageState BlankPageState(string id, ProfileTemplate template)
    {
        var state = new ProfilePageState
        {
            Id = NormalizePageId(id),
            Manifest = new PageManifest()
        };
        state.Manifest.Controllers = ControllersForState(state, template);
        return state;
    }

    private static List<string> GetExportPageIds(ProfileArchive profile)
    {
        var pageIds = profile.PageOrder.Select(NormalizePageId).Where(id => profile.GetPageState(id) is not null).ToList();
        if (pageIds.Count == 0 && profile.GetPageState(profile.ActivePageId) is not null)
            pageIds = [NormalizePageId(profile.ActivePageId)];
        if (pageIds.Count == 0)
            pageIds = [NormalizePageId(profile.Preset.WorkingPageId)];
        return OrderedUniquePageIds(pageIds);
    }

    private static List<string> GetReferencedFolderIds(ProfileArchive profile)
    {
        var ids = new HashSet<string>();
        foreach (var action in profile.AllActions())
        {
            var id = GetFolderProfileId(action)?.Trim();
            if (!string.IsNullOrEmpty(id))
                ids.Add(NormalizePageId(id));
        }
        return [.. ids];
    }

    private static void CanonicalizePageFolders(string pagesRoot, HashSet<string> allowedPageIds)
    {
        if (!Directory.Exists(pagesRoot)) return;

        var allowedUpper = new HashSet<string>(allowedPageIds.Select(id => NormalizePageId(id).ToUpperInvariant()), StringComparer.OrdinalIgnoreCase);

        // Remove disallowed folders
        foreach (var dir in Directory.GetDirectories(pagesRoot))
        {
            var name = Path.GetFileName(dir);
            if (!allowedUpper.Contains(name))
                Directory.Delete(dir, recursive: true);
        }

        // Rename to canonical case
        foreach (var dir in Directory.GetDirectories(pagesRoot))
        {
            var name = Path.GetFileName(dir);
            var canonical = allowedUpper.FirstOrDefault(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
            if (canonical is null || canonical == name) continue;

            var canonicalPath = Path.Combine(pagesRoot, canonical);
            if (!Directory.Exists(canonicalPath))
            {
                var tempPath = Path.Combine(pagesRoot, $".rename-{Guid.NewGuid()}");
                Directory.Move(dir, tempPath);
                Directory.Move(tempPath, canonicalPath);
            }
            else
            {
                FileHelper.MergeDirectory(dir, canonicalPath);
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    private static void WriteJson<T>(T value, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir is not null) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(value, _jsonWriteOptions));
    }

    private static string MakeWorkingDirectory(string prefix = "profile")
    {
        var root = Path.Combine(Path.GetTempPath(), "SDProfileManager", $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static string? GetPluginUuid(JsonNode action) =>
        action.GetProperty("Plugin")?.GetProperty("UUID").GetStringValue();

    private static string? GetFolderProfileId(JsonNode action) =>
        action.GetProperty("Settings")?.GetProperty("ProfileUUID").GetStringValue();

    private static List<string> GetReferencedImagePaths(JsonNode action)
    {
        var results = new HashSet<string>();
        WalkForImages(action, results);
        return [.. results];
    }

    private static void WalkForImages(JsonNode? node, HashSet<string> results)
    {
        if (node is null) return;

        if (node is JsonObject obj)
        {
            foreach (var prop in obj)
                WalkForImages(prop.Value, results);
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
                WalkForImages(item, results);
        }
        else if (node is JsonValue val && val.TryGetValue(out string? str) && str is not null)
        {
            if (str.Contains("Images/") || str.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                || str.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                || str.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                results.Add(str);
            }
        }
    }

    private static string NormalizePageId(string value) =>
        value.Trim().ToLowerInvariant();

    private static List<string> OrderedUniquePageIds(IEnumerable<string> values)
    {
        var seen = new HashSet<string>();
        var output = new List<string>();
        foreach (var value in values)
        {
            var normalized = NormalizePageId(value);
            if (string.IsNullOrEmpty(normalized) || !seen.Add(normalized)) continue;
            output.Add(normalized);
        }
        return output;
    }
}
