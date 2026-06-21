// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Serializer.Converters;

/// <summary>
/// Converts <see cref="Guid"/> values from any of the standard string formats
/// (<c>N</c>, <c>D</c>, <c>B</c>, <c>P</c>, <c>X</c>) in addition to the built-in
/// <see cref="Utf8JsonReader.GetGuid"/> path. Serializes as the default <c>D</c> format
/// (<c>xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx</c>).
/// </summary>
public sealed class StringToGuidJsonConverter : JsonConverter<Guid>
{
    public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            foreach (var format in (ReadOnlySpan<string>)["N", "D", "B", "P", "X"])
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
