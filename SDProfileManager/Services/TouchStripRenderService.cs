using System.Globalization;
using System.Text.Json.Nodes;
using SDProfileManager.Helpers;
using SDProfileManager.Models;

namespace SDProfileManager.Services;

public class TouchStripRenderService
{
    private readonly PluginCatalogService _pluginCatalog;
    private readonly TouchStripLayoutService _layoutService;

    public TouchStripRenderService(PluginCatalogService pluginCatalog, TouchStripLayoutService layoutService)
    {
        _pluginCatalog = pluginCatalog;
        _layoutService = layoutService;
    }

    public TouchStripRenderModel BuildSegmentRender(ProfileArchive profile, JsonNode? action, string coordinate, string? pageId = null)
    {
        var resolvedPageId = pageId ?? profile.ActivePageId;

        if (action is null)
        {
            return new TouchStripRenderModel
            {
                Mode = TouchStripRenderMode.Fallback,
                Availability = PluginRenderAvailability.ProfileOnly,
                PrimaryLabel = "Empty",
                SecondaryLabel = "No action",
                TooltipText = $"Touch strip segment\nDrop an encoder action here.\nCoordinate: {coordinate}"
            };
        }

        var presentation = profile.GetActionPresentation(action);
        var primaryLabel = ResolvePrimaryLabel(presentation);
        var secondaryLabel = ResolveSecondaryLabel(presentation, primaryLabel);
        var model = new TouchStripRenderModel
        {
            Mode = TouchStripRenderMode.Fallback,
            Availability = PluginRenderAvailability.ProfileOnly,
            PrimaryLabel = primaryLabel,
            SecondaryLabel = secondaryLabel,
            PrimaryIconPath = ResolveProfileIconPath(profile, action, resolvedPageId)
        };

        var pluginUuid = action.GetProperty("Plugin")?.GetProperty("UUID").GetStringValue();
        var actionUuid = action.GetProperty("UUID").GetStringValue();
        var pluginDefinition = _pluginCatalog.ResolveAction(pluginUuid, actionUuid);
        model.Availability = pluginDefinition.Availability;

        if (model.PrimaryIconPath is null && !string.IsNullOrWhiteSpace(pluginDefinition.PluginFolderPath) && !string.IsNullOrWhiteSpace(pluginDefinition.EncoderIconPath))
        {
            model.PrimaryIconPath = _layoutService.ResolveAssetPath(pluginDefinition.PluginFolderPath, pluginDefinition.EncoderIconPath);
        }

        if (pluginDefinition.Availability == PluginRenderAvailability.LayoutAvailable
            && !string.IsNullOrWhiteSpace(pluginDefinition.PluginFolderPath)
            && !string.IsNullOrWhiteSpace(pluginDefinition.LayoutPath))
        {
            var layout = _layoutService.TryLoadLayout(pluginDefinition.PluginFolderPath, pluginDefinition.LayoutPath);
            if (layout is not null)
            {
                model.Layers = BuildLayers(layout, pluginDefinition, model, action);
                if (model.Layers.Count > 0)
                    model.Mode = TouchStripRenderMode.Layout;
            }
            else
            {
                model.Availability = PluginRenderAvailability.LayoutMissing;
            }
        }

        model.BadgeText = BuildBadge(model.Availability);
        model.TooltipText = BuildTooltip(model, coordinate, pluginDefinition.Message);
        return model;
    }

    private List<TouchStripRenderLayer> BuildLayers(
        TouchStripLayoutModel layout,
        PluginActionDefinition pluginDefinition,
        TouchStripRenderModel model,
        JsonNode action)
    {
        var layers = new List<TouchStripRenderLayer>();

        foreach (var item in layout.Items.OrderBy(i => i.ZOrder))
        {
            if (!item.Enabled)
                continue;

            var kind = item.Type.Trim().ToLowerInvariant();
            switch (kind)
            {
                case "pixmap":
                {
                    var imagePath = ResolvePixmapPath(item, pluginDefinition, model.PrimaryIconPath);
                    if (imagePath is not null)
                    {
                        layers.Add(new TouchStripRenderLayer
                        {
                            Key = item.Key,
                            Type = "image",
                            Rect = item.Rect,
                            ZOrder = item.ZOrder,
                            ImagePath = imagePath,
                            Background = item.Background
                        });
                    }
                    else
                    {
                        layers.Add(new TouchStripRenderLayer
                        {
                            Key = item.Key,
                            Type = "placeholder",
                            Rect = item.Rect,
                            ZOrder = item.ZOrder,
                            Text = PlaceholderForKey(item.Key),
                            Alignment = item.Alignment,
                            FontSize = item.Font?.Size,
                            FontWeight = item.Font?.Weight,
                            Background = item.Background
                        });
                    }

                    break;
                }
                case "text":
                {
                    layers.Add(new TouchStripRenderLayer
                    {
                        Key = item.Key,
                        Type = "text",
                        Rect = item.Rect,
                        ZOrder = item.ZOrder,
                        Text = ResolveText(item, action, model),
                        Alignment = item.Alignment,
                        FontSize = item.Font?.Size,
                        FontWeight = item.Font?.Weight,
                        Background = item.Background
                    });
                    break;
                }
            }
        }

        return layers;
    }

    private string? ResolvePixmapPath(TouchStripLayoutItem item, PluginActionDefinition pluginDefinition, string? primaryIconPath)
    {
        var key = item.Key.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(item.Value) && !string.IsNullOrWhiteSpace(pluginDefinition.PluginFolderPath))
        {
            var resolved = _layoutService.ResolveAssetPath(pluginDefinition.PluginFolderPath, item.Value!);
            if (resolved is not null)
                return resolved;
        }

        if (key == "icon" && !string.IsNullOrWhiteSpace(primaryIconPath))
            return primaryIconPath;

        if (key.Contains("icon", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(pluginDefinition.PluginFolderPath)
            && !string.IsNullOrWhiteSpace(pluginDefinition.EncoderIconPath))
        {
            return _layoutService.ResolveAssetPath(pluginDefinition.PluginFolderPath, pluginDefinition.EncoderIconPath);
        }

        return null;
    }

    private static string ResolveText(TouchStripLayoutItem item, JsonNode action, TouchStripRenderModel model)
    {
        var key = item.Key.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(item.Value))
            return item.Value!;

        return key switch
        {
            "title" => model.PrimaryLabel,
            "value" => ExtractNumericValue(action) ?? "--",
            "unit" => ExtractUnit(action) ?? "--",
            _ => string.Empty
        };
    }

    private static string? ExtractNumericValue(JsonNode action)
    {
        var settings = action.GetProperty("Settings")?.GetObjectValue();
        if (settings is null)
            return null;

        var keys = new[] { "setValue", "value", "volume", "level", "gain" };
        foreach (var key in keys)
        {
            if (!settings.TryGetPropertyValue(key, out var value) || value is null)
                continue;

            if (value is JsonValue scalar)
            {
                if (scalar.TryGetValue(out int i))
                    return i.ToString(CultureInfo.InvariantCulture);
                if (scalar.TryGetValue(out double d))
                    return Math.Round(d).ToString(CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private static string? ExtractUnit(JsonNode action)
    {
        var settings = action.GetProperty("Settings")?.GetObjectValue();
        if (settings is null)
            return null;

        var keys = new[] { "unit", "suffix" };
        foreach (var key in keys)
        {
            if (!settings.TryGetPropertyValue(key, out var value) || value is null)
                continue;

            if (value is JsonValue scalar && scalar.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text))
                return text;
        }

        return null;
    }

    private static string PlaceholderForKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return "--";

        return key.Trim() switch
        {
            "dial" => "(dial)",
            "levelmeter" => "--",
            "mainIndicator" => "--",
            _ => "--"
        };
    }

    private static string BuildBadge(PluginRenderAvailability availability) => availability switch
    {
        PluginRenderAvailability.LayoutEncrypted => "Locked",
        PluginRenderAvailability.LayoutMissing => "No Layout",
        PluginRenderAvailability.PluginMissing => "No Plugin",
        _ => string.Empty
    };

    private static string BuildTooltip(TouchStripRenderModel model, string coordinate, string availabilityMessage)
    {
        var lines = new List<string>
        {
            $"Action: {model.PrimaryLabel}",
            $"Plugin: {model.SecondaryLabel}",
            $"Coordinate: {coordinate}"
        };

        if (!string.IsNullOrWhiteSpace(model.BadgeText))
            lines.Add($"Layout: {model.BadgeText}");
        if (!string.IsNullOrWhiteSpace(availabilityMessage))
            lines.Add(availabilityMessage);

        return string.Join("\n", lines);
    }

    private static string ResolvePrimaryLabel(ActionPresentation presentation)
    {
        if (!string.IsNullOrWhiteSpace(presentation.DisplayName))
            return presentation.DisplayName;
        if (!string.IsNullOrWhiteSpace(presentation.ActionName))
            return presentation.ActionName;
        if (!string.IsNullOrWhiteSpace(presentation.Title))
            return presentation.Title;
        return "Action";
    }

    private static string ResolveSecondaryLabel(ActionPresentation presentation, string primaryLabel)
    {
        if (string.IsNullOrWhiteSpace(presentation.PluginName))
            return string.Empty;
        if (primaryLabel.Contains(presentation.PluginName, StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return presentation.PluginName;
    }

    private static string? ResolveProfileIconPath(ProfileArchive profile, JsonNode action, string pageId)
    {
        var refs = new List<string>();

        var obj = action.GetObjectValue();
        if (obj is not null)
        {
            var stateIndex = obj["State"].GetIntValue() ?? 0;
            var states = obj["States"].GetArrayValue();
            if (states is not null && stateIndex >= 0 && stateIndex < states.Count)
            {
                var stateObject = states[stateIndex]?.GetObjectValue();
                var imageRef = stateObject?["Image"].GetStringValue();
                if (!string.IsNullOrWhiteSpace(imageRef))
                    refs.Add(imageRef);
            }

            var encoderIconRef = obj["Encoder"]?.GetObjectValue()?["Icon"].GetStringValue();
            if (!string.IsNullOrWhiteSpace(encoderIconRef))
                refs.Add(encoderIconRef);

            var resources = ExtractImageReferences(obj["Resources"]);
            refs.AddRange(resources);
        }

        var presentationImage = profile.GetActionPresentation(action).ImageReference;
        if (!string.IsNullOrWhiteSpace(presentationImage))
            refs.Add(presentationImage);

        foreach (var imageRef in refs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var resolved = profile.ResolveImagePath(imageRef, pageId);
            if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved))
                return resolved;
        }

        return null;
    }

    private static IEnumerable<string> ExtractImageReferences(JsonNode? node)
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Walk(node, results);
        return results;
    }

    private static void Walk(JsonNode? node, HashSet<string> output)
    {
        if (node is null)
            return;

        switch (node)
        {
            case JsonObject obj:
                foreach (var property in obj)
                    Walk(property.Value, output);
                break;
            case JsonArray arr:
                foreach (var item in arr)
                    Walk(item, output);
                break;
            case JsonValue value when value.TryGetValue(out string? text):
                if (!string.IsNullOrWhiteSpace(text)
                    && (text.Contains("Images/", StringComparison.OrdinalIgnoreCase)
                        || text.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                        || text.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                        || text.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                        || text.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)))
                {
                    output.Add(text);
                }
                break;
        }
    }
}
