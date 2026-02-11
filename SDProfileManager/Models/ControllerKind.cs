using System.Text.Json;
using System.Text.Json.Serialization;

namespace SDProfileManager.Models;

[JsonConverter(typeof(ControllerKindJsonConverter))]
public enum ControllerKind
{
    Keypad,
    Encoder,
    Neo
}

public static class ControllerKindExtensions
{
    public static string ToJsonString(this ControllerKind kind) => kind switch
    {
        ControllerKind.Keypad => "Keypad",
        ControllerKind.Encoder => "Encoder",
        ControllerKind.Neo => "Neo",
        _ => "Keypad"
    };

    public static ControllerKind FromJsonString(string value) => value switch
    {
        "Keypad" => ControllerKind.Keypad,
        "Encoder" => ControllerKind.Encoder,
        "Neo" => ControllerKind.Neo,
        _ => ControllerKind.Keypad
    };
}

public class ControllerKindJsonConverter : JsonConverter<ControllerKind>
{
    public override ControllerKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString() ?? "Keypad";
        return ControllerKindExtensions.FromJsonString(value);
    }

    public override void Write(Utf8JsonWriter writer, ControllerKind value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToJsonString());
    }
}
