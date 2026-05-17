// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;

namespace Headless.Serializer.Converters;

[RequiresUnreferencedCode(
    "JSON serialization and deserialization might require types that cannot be statically analyzed."
)]
[RequiresDynamicCode("JSON serialization and deserialization might require runtime code generation.")]
public sealed class EmptyStringAsNullJsonConverter<T> : JsonConverter<T?>
    where T : class
{
    private static readonly ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> _FallbackOptions = [];

    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType is JsonTokenType.String && reader.ValueTextEquals(ReadOnlySpan<byte>.Empty))
        {
            return null;
        }

        // Delegate via a clone that excludes this converter to avoid infinite recursion when
        // STJ resolves the converter for T and lands back on us.
        var fallback = _FallbackOptions.GetValue(options, _CreateFallbackOptions);

        return JsonSerializer.Deserialize<T>(ref reader, fallback);
    }

    public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
    {
        if (value is null or "")
        {
            writer.WriteNullValue();

            return;
        }

        var fallback = _FallbackOptions.GetValue(options, _CreateFallbackOptions);

        JsonSerializer.Serialize(writer, value, fallback);
    }

    private static JsonSerializerOptions _CreateFallbackOptions(JsonSerializerOptions source)
    {
        var clone = new JsonSerializerOptions(source);

        for (var i = clone.Converters.Count - 1; i >= 0; i--)
        {
            if (clone.Converters[i] is EmptyStringAsNullJsonConverter<T>)
            {
                clone.Converters.RemoveAt(i);
            }
        }

        return clone;
    }
}
