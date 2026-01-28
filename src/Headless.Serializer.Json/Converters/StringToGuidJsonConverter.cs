// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Serializer.Converters;

public sealed class StringToGuidJsonConverter : JsonConverter<Guid>
{
    public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            ReadOnlySpan<string> formats = ["N", "D", "B", "P", "X"];

            foreach (var format in formats)
            {
                if (Guid.TryParseExact(text, format, out var parsedGuid))
                {
                    return parsedGuid;
                }
            }
        }

        return reader.GetGuid();
    }

    public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}
