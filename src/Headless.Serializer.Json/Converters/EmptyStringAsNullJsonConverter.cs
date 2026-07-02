// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;

namespace Headless.Serializer.Converters;

/// <summary>
/// Treats an empty JSON string (<c>""</c>) as <see langword="null"/> during deserialization, and writes
/// <see langword="null"/> (or an empty string value) as a JSON <see langword="null"/> token during serialization.
/// </summary>
/// <remarks>
/// To avoid infinite recursion the converter internally clones the active <see cref="JsonSerializerOptions"/>
/// with itself removed and delegates actual value reading and writing to the resulting fallback options. The
/// cloned options instance is cached per source options instance via <see cref="System.Runtime.CompilerServices.ConditionalWeakTable{TKey, TValue}"/>.
/// </remarks>
/// <typeparam name="T">A reference type whose empty-string representation should map to <see langword="null"/>.</typeparam>
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
