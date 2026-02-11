using System.Text.Json;
using System.Text.Json.Nodes;
using SDProfileManager.Models;

namespace SDProfileManager.Services;

public class PluginCatalogService
{
    private readonly string _pluginRootPath;
    private readonly Dictionary<string, PluginManifestCacheEntry> _manifestCache = new(StringComparer.OrdinalIgnoreCase);

    public PluginCatalogService(string? pluginRootPath = null)
    {
        var defaultRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Elgato",
            "StreamDeck",
            "Plugins");
        _pluginRootPath = string.IsNullOrWhiteSpace(pluginRootPath) ? defaultRoot : pluginRootPath;
    }

    public string PluginRootPath => _pluginRootPath;

    public PluginActionDefinition ResolveAction(string? pluginUuid, string? actionUuid)
    {
        var normalizedPluginUuid = pluginUuid?.Trim() ?? string.Empty;
        var normalizedActionUuid = actionUuid?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedPluginUuid))
        {
            return new PluginActionDefinition
            {
                Availability = PluginRenderAvailability.LayoutMissing,
                Message = "Plugin UUID missing from action."
            };
        }

        if (!Directory.Exists(_pluginRootPath))
        {
            return new PluginActionDefinition
            {
                Availability = PluginRenderAvailability.PluginMissing,
                PluginUuid = normalizedPluginUuid,
                Message = $"Plugin root not found: {_pluginRootPath}"
            };
        }

        var pluginFolderPath = Path.Combine(_pluginRootPath, $"{normalizedPluginUuid}.sdPlugin");
        if (!Directory.Exists(pluginFolderPath))
        {
            return new PluginActionDefinition
            {
                Availability = PluginRenderAvailability.PluginMissing,
                PluginUuid = normalizedPluginUuid,
                PluginFolderPath = pluginFolderPath,
                Message = "Plugin package not installed locally."
            };
        }

        var manifestPath = Path.Combine(pluginFolderPath, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return new PluginActionDefinition
            {
                Availability = PluginRenderAvailability.LayoutMissing,
                PluginUuid = normalizedPluginUuid,
                PluginFolderPath = pluginFolderPath,
                Message = "Plugin manifest missing."
            };
        }

        var manifest = GetManifest(normalizedPluginUuid, manifestPath);
        if (manifest.Availability == PluginRenderAvailability.LayoutEncrypted)
        {
            return new PluginActionDefinition
            {
                Availability = PluginRenderAvailability.LayoutEncrypted,
                PluginUuid = normalizedPluginUuid,
                PluginFolderPath = pluginFolderPath,
                Message = manifest.ErrorMessage ?? "Plugin manifest is encrypted or unreadable."
            };
        }

        if (manifest.Root is null)
        {
            return new PluginActionDefinition
            {
                Availability = PluginRenderAvailability.LayoutMissing,
                PluginUuid = normalizedPluginUuid,
                PluginFolderPath = pluginFolderPath,
                Message = manifest.ErrorMessage ?? "Plugin manifest could not be parsed."
            };
        }

        if (string.IsNullOrWhiteSpace(normalizedActionUuid))
        {
            return new PluginActionDefinition
            {
                Availability = PluginRenderAvailability.LayoutMissing,
                PluginUuid = normalizedPluginUuid,
                PluginFolderPath = pluginFolderPath,
                Message = "Action UUID missing from action payload."
            };
        }

        var actionObject = FindActionDefinition(manifest.Root, normalizedActionUuid);
        if (actionObject is null)
        {
            return new PluginActionDefinition
            {
                Availability = PluginRenderAvailability.LayoutMissing,
                PluginUuid = normalizedPluginUuid,
                PluginFolderPath = pluginFolderPath,
                Message = $"Action {normalizedActionUuid} not found in plugin manifest."
            };
        }

        var encoderObject = actionObject["Encoder"] as JsonObject;
        var layoutPath = encoderObject?["layout"]?.GetValue<string>()?.Trim();
        var encoderIconPath = encoderObject?["icon"]?.GetValue<string>()?.Trim();

        if (string.IsNullOrWhiteSpace(layoutPath))
        {
            return new PluginActionDefinition
            {
                Availability = PluginRenderAvailability.LayoutMissing,
                PluginUuid = normalizedPluginUuid,
                PluginFolderPath = pluginFolderPath,
                EncoderIconPath = encoderIconPath,
                Message = "Plugin action has no encoder layout."
            };
        }

        return new PluginActionDefinition
        {
            Availability = PluginRenderAvailability.LayoutAvailable,
            PluginUuid = normalizedPluginUuid,
            PluginFolderPath = pluginFolderPath,
            LayoutPath = layoutPath,
            EncoderIconPath = encoderIconPath,
            Message = "Plugin layout available."
        };
    }

    private PluginManifestCacheEntry GetManifest(string pluginUuid, string manifestPath)
    {
        if (_manifestCache.TryGetValue(pluginUuid, out var cached))
            return cached;

        var loaded = LoadManifest(manifestPath);
        _manifestCache[pluginUuid] = loaded;
        return loaded;
    }

    private static PluginManifestCacheEntry LoadManifest(string manifestPath)
    {
        try
        {
            if (IsEncryptedManifest(manifestPath))
            {
                return new PluginManifestCacheEntry
                {
                    Availability = PluginRenderAvailability.LayoutEncrypted,
                    ErrorMessage = "Manifest is ELGATO packaged/encrypted."
                };
            }

            var json = File.ReadAllText(manifestPath);
            var root = JsonNode.Parse(json) as JsonObject;
            if (root is null)
            {
                return new PluginManifestCacheEntry
                {
                    Availability = PluginRenderAvailability.LayoutMissing,
                    ErrorMessage = "Manifest JSON root is invalid."
                };
            }

            return new PluginManifestCacheEntry
            {
                Availability = PluginRenderAvailability.LayoutAvailable,
                Root = root
            };
        }
        catch (JsonException ex)
        {
            return new PluginManifestCacheEntry
            {
                Availability = PluginRenderAvailability.LayoutEncrypted,
                ErrorMessage = $"Manifest JSON parse failed: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new PluginManifestCacheEntry
            {
                Availability = PluginRenderAvailability.LayoutMissing,
                ErrorMessage = $"Manifest load failed: {ex.Message}"
            };
        }
    }

    private static bool IsEncryptedManifest(string manifestPath)
    {
        try
        {
            using var stream = File.OpenRead(manifestPath);
            Span<byte> header = stackalloc byte[6];
            var bytesRead = stream.Read(header);
            if (bytesRead < 6)
                return false;
            return header.SequenceEqual("ELGATO"u8);
        }
        catch
        {
            return false;
        }
    }

    private static JsonObject? FindActionDefinition(JsonObject manifestRoot, string actionUuid)
    {
        if (manifestRoot["Actions"] is not JsonArray actions)
            return null;

        foreach (var actionNode in actions)
        {
            if (actionNode is not JsonObject actionObject)
                continue;

            var uuid = actionObject["UUID"]?.GetValue<string>();
            if (string.Equals(uuid, actionUuid, StringComparison.OrdinalIgnoreCase))
                return actionObject;
        }

        return null;
    }

    private class PluginManifestCacheEntry
    {
        public PluginRenderAvailability Availability { get; set; }
        public JsonObject? Root { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
