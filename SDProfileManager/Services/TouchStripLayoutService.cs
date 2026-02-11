using System.Text.Json.Nodes;
using SDProfileManager.Helpers;
using SDProfileManager.Models;

namespace SDProfileManager.Services;

public class TouchStripLayoutService
{
    private readonly Dictionary<string, TouchStripLayoutModel?> _layoutCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _assetPathCache = new(StringComparer.OrdinalIgnoreCase);

    public TouchStripLayoutModel? TryLoadLayout(string pluginFolderPath, string layoutPath)
    {
        if (string.IsNullOrWhiteSpace(pluginFolderPath) || string.IsNullOrWhiteSpace(layoutPath))
            return null;

        var key = $"{pluginFolderPath}|{layoutPath}".ToLowerInvariant();
        if (_layoutCache.TryGetValue(key, out var cached))
            return cached;

        var absoluteLayoutPath = ResolveAssetPath(pluginFolderPath, layoutPath);
        if (absoluteLayoutPath is null || !File.Exists(absoluteLayoutPath))
        {
            _layoutCache[key] = null;
            return null;
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(absoluteLayoutPath)) as JsonObject;
            if (root is null)
            {
                _layoutCache[key] = null;
                return null;
            }

            var model = new TouchStripLayoutModel
            {
                Id = root["id"]?.GetValue<string>() ?? string.Empty
            };

            if (root["items"] is JsonArray items)
            {
                foreach (var node in items)
                {
                    if (node is not JsonObject obj)
                        continue;

                    var item = ParseItem(obj);
                    if (item is not null)
                        model.Items.Add(item);
                }
            }

            _layoutCache[key] = model;
            return model;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Touch strip layout parse failed layout={layoutPath} error={ex.Message}");
            _layoutCache[key] = null;
            return null;
        }
    }

    public string? ResolveAssetPath(string pluginFolderPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(pluginFolderPath) || string.IsNullOrWhiteSpace(relativePath))
            return null;

        if (Path.IsPathRooted(relativePath) && File.Exists(relativePath))
            return relativePath;

        var cacheKey = $"{pluginFolderPath}|{relativePath}".ToLowerInvariant();
        if (_assetPathCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var normalized = relativePath.Replace('\\', '/');
        if (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];

        var resolved = FileHelper.ResolveCaseInsensitivePath(pluginFolderPath, normalized);
        _assetPathCache[cacheKey] = resolved;
        return resolved;
    }

    private static TouchStripLayoutItem? ParseItem(JsonObject obj)
    {
        var type = obj["type"]?.GetValue<string>()?.Trim();
        if (string.IsNullOrWhiteSpace(type))
            return null;

        var rect = ParseRect(obj["rect"] as JsonArray);
        if (rect is null)
            return null;

        var item = new TouchStripLayoutItem
        {
            Key = obj["key"]?.GetValue<string>() ?? string.Empty,
            Type = type,
            Rect = rect,
            ZOrder = obj["zOrder"]?.GetValue<int>() ?? 0,
            Value = obj["value"]?.GetValue<string>(),
            Enabled = obj["enabled"]?.GetValue<bool>() ?? true,
            Alignment = obj["alignment"]?.GetValue<string>(),
            Background = obj["background"]?.GetValue<string>(),
            Font = ParseFont(obj["font"] as JsonObject)
        };

        return item;
    }

    private static TouchStripRect? ParseRect(JsonArray? array)
    {
        if (array is null || array.Count != 4)
            return null;

        var values = new int[4];
        for (var i = 0; i < 4; i++)
        {
            if (!TryGetInt(array[i], out values[i]))
                return null;
        }

        return new TouchStripRect
        {
            X = values[0],
            Y = values[1],
            Width = values[2],
            Height = values[3]
        };
    }

    private static TouchStripFontSpec? ParseFont(JsonObject? font)
    {
        if (font is null)
            return null;

        var result = new TouchStripFontSpec();
        if (TryGetDouble(font["size"], out var size))
            result.Size = size;
        if (TryGetInt(font["weight"], out var weight))
            result.Weight = weight;
        return result;
    }

    private static bool TryGetInt(JsonNode? node, out int value)
    {
        value = 0;
        if (node is null)
            return false;

        if (node is JsonValue scalar)
        {
            if (scalar.TryGetValue(out int i))
            {
                value = i;
                return true;
            }

            if (scalar.TryGetValue(out double d))
            {
                value = (int)Math.Round(d);
                return true;
            }
        }

        return false;
    }

    private static bool TryGetDouble(JsonNode? node, out double value)
    {
        value = 0;
        if (node is null)
            return false;

        if (node is JsonValue scalar)
        {
            if (scalar.TryGetValue(out double d))
            {
                value = d;
                return true;
            }

            if (scalar.TryGetValue(out int i))
            {
                value = i;
                return true;
            }
        }

        return false;
    }
}
