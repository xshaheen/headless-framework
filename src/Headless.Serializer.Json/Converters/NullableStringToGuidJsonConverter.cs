// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Serializer.Converters;

/// <summary>
/// Converts nullable <see cref="Guid"/> values from any of the standard string formats
/// (<c>N</c>, <c>D</c>, <c>B</c>, <c>P</c>, <c>X</c>) and from JSON <c>null</c>.
/// Serializes as the default <c>D</c> format (<c>xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx</c>)
/// or as JSON <c>null</c> when the value is absent.
/// </summary>
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
