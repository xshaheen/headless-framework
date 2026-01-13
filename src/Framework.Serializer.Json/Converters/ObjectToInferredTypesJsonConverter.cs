// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace Framework.Serializer.Converters;

/// <summary>
/// See <a href="https://docs.microsoft.com/en-us/dotnet/standard/serialization/system-text-json-converters-how-to#deserialize-inferred-types-to-object-properties">Deserialize inferred types to object properties</a>
/// </summary>
[RequiresUnreferencedCode("JSON serialization and deserialization might require types that cannot be statically analyzed.")]
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
            JsonTokenType.Number => reader.GetDouble(),
            JsonTokenType.String when reader.TryGetDateTimeOffset(out var datetime) => datetime,
            JsonTokenType.String => reader.GetString(),
            _ => JsonDocument.ParseValue(ref reader).RootElement.Clone(),
        };
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
