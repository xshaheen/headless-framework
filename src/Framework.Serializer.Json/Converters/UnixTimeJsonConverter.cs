// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Framework.Serializer.Json.Converters;

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
