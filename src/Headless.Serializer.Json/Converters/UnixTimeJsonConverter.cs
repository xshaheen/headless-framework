// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Serializer.Converters;

public sealed class UnixTimeJsonConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var unixTime =
            reader.TokenType is JsonTokenType.Number
                ? reader.GetInt64()
                : long.Parse(reader.GetString()!, CultureInfo.InvariantCulture);

        return DateTimeOffset.FromUnixTimeSeconds(unixTime);
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value.ToUnixTimeSeconds());
    }
}
