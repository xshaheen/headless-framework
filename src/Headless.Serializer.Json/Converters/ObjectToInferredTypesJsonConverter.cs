// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Serializer.Converters;

/// <summary>
/// Deserializes <see langword="object"/>-typed properties to their most appropriate .NET type by inspecting the
/// JSON token: booleans become <see cref="bool"/>, integers that fit become <see cref="long"/>, other numbers become
/// <see cref="decimal"/> (or <see cref="double"/> when <see cref="decimal"/> cannot hold them), strings remain
/// <see cref="string"/>, and objects and arrays are returned as a <see cref="System.Text.Json.JsonElement"/>.
/// </summary>
/// <remarks>
/// <para>
/// Strings are never reinterpreted: JSON has no date type, so a string such as <c>"2024-01-01"</c> stays the string it
/// was written as, and a reader that expects a date parses it. Non-integer numbers prefer <see cref="decimal"/> so a
/// written <see cref="decimal"/> reads back exactly; a <see cref="double"/> reads back as the same value after
/// conversion.
/// </para>
/// See
/// <see href="https://docs.microsoft.com/en-us/dotnet/standard/serialization/system-text-json-converters-how-to#deserialize-inferred-types-to-object-properties">Deserialize inferred types to object properties</see>
/// for background.
/// </remarks>
[RequiresUnreferencedCode(
    "JSON serialization and deserialization might require types that cannot be statically analyzed."
)]
[RequiresDynamicCode("JSON serialization and deserialization might require runtime code generation.")]
public sealed class ObjectToInferredTypesJsonConverter : JsonConverter<object?>
{
    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Number when reader.TryGetInt64(out var l) => l,
            JsonTokenType.Number => _ReadNonInteger(ref reader),
            JsonTokenType.String => reader.GetString(),
            _ => JsonDocument.ParseValue(ref reader).RootElement.Clone(),
        };
    }

    private static object _ReadNonInteger(ref Utf8JsonReader reader)
    {
        var number = reader.GetDouble();

        // decimal keeps every written digit of a decimal value, but it underflows or truncates numbers that are very
        // small or carry more digits than it holds; those stay double so the read never loses more than double would.
        return reader.TryGetDecimal(out var exact) && (double)exact == number ? exact : number;
    }

    public override void Write(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();

            return;
        }

        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
