// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Serializer.Converters;

public sealed class NullableStringToGuidJsonConverter : JsonConverter<Guid?>
{
    public override Guid? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.String)
        {
            var guidString = reader.GetString();
            ReadOnlySpan<string> formats = ["N", "D", "B", "P", "X"];

            foreach (var format in formats)
            {
                if (Guid.TryParseExact(guidString, format, out var parsedGuid))
                {
                    return parsedGuid;
                }
            }
        }

        if (reader.TokenType is JsonTokenType.Null)
        {
            return null;
        }

        return reader.GetGuid();
    }

    public override void Write(Utf8JsonWriter writer, Guid? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value.Value);
        }
    }
}
