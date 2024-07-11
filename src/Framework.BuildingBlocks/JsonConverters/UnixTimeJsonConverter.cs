using System.Text.Json;
using System.Text.Json.Serialization;

namespace Framework.BuildingBlocks.JsonConverters;

public sealed class UnixTimeJsonConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        long unixTime;

        if (reader.TokenType is JsonTokenType.Number)
        {
            unixTime = reader.GetInt64();
        }
        else
        {
            unixTime = long.Parse(reader.GetString()!, CultureInfo.InvariantCulture);
        }

        return DateTimeOffset.FromUnixTimeSeconds(unixTime);
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value.ToUnixTimeSeconds());
    }
}
