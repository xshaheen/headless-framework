// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace Framework.Serializer.Converters;

[RequiresUnreferencedCode(
    "JSON serialization and deserialization might require types that cannot be statically analyzed."
)]
[RequiresDynamicCode("JSON serialization and deserialization might require runtime code generation.")]
public sealed class EmptyStringAsNullJsonConverter<T> : JsonConverter<T?>
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
        if (value is "")
        {
            writer.WriteNullValue();

            return;
        }

        JsonSerializer.Serialize(writer, value, options);
    }
}
