using System.Text.Json;
using System.Text.Json.Serialization;

namespace Framework.BuildingBlocks.JsonConverters;

public sealed class EmptyStringAsNullConverter<T> : JsonConverter<T?>
    where T : class
{
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        ReadOnlySpan<byte> empty = [];

        if (reader.TokenType is JsonTokenType.String)
        {
            if (reader.ValueTextEquals(empty))
            {
                return null;
            }
        }

        return JsonSerializer.Deserialize<T>(ref reader, options);
    }

    public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, options);
    }
}
