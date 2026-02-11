using System.Text.Json.Nodes;

namespace SDProfileManager.Helpers;

public static class JsonHelper
{
    public static string? GetStringValue(this JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue(out string? s))
            return s;
        return null;
    }

    public static int? GetIntValue(this JsonNode? node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue(out int i)) return i;
            if (value.TryGetValue(out double d)) return (int)d;
            if (value.TryGetValue(out long l)) return (int)l;
        }
        return null;
    }

    public static double? GetDoubleValue(this JsonNode? node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue(out double d)) return d;
            if (value.TryGetValue(out int i)) return i;
        }
        return null;
    }

    public static JsonObject? GetObjectValue(this JsonNode? node)
    {
        return node as JsonObject;
    }

    public static JsonArray? GetArrayValue(this JsonNode? node)
    {
        return node as JsonArray;
    }

    public static JsonNode? GetProperty(this JsonNode? node, string key)
    {
        if (node is JsonObject obj && obj.TryGetPropertyValue(key, out var value))
            return value;
        return null;
    }

    public static JsonNode DeepClone(this JsonNode node)
    {
        return JsonNode.Parse(node.ToJsonString())!;
    }

    public static Dictionary<string, JsonNode> DeepCloneActions(this Dictionary<string, JsonNode> actions)
    {
        var result = new Dictionary<string, JsonNode>(actions.Count);
        foreach (var (key, value) in actions)
        {
            result[key] = value.DeepClone();
        }
        return result;
    }
}
