// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;

namespace Headless.Serializer.Converters;

/// <summary>
/// Converts <see cref="IEnumerable{T}"/> collections by applying a custom per-item converter
/// (<typeparamref name="TConverterType"/>) to each element, overriding whatever converter the parent
/// <see cref="JsonSerializerOptions"/> would normally select for <typeparamref name="TDatatype"/>.
/// </summary>
/// <typeparam name="TDatatype">The collection element type.</typeparam>
/// <typeparam name="TConverterType">
/// The <see cref="JsonConverter"/> type to use for individual items. Must have a public parameterless constructor.
/// </typeparam>
[RequiresUnreferencedCode(
    "JSON serialization and deserialization might require types that cannot be statically analyzed."
)]
[RequiresDynamicCode("JSON serialization and deserialization might require runtime code generation.")]
public sealed class CollectionItemJsonConverter<
    TDatatype,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TConverterType
> : JsonConverter<IEnumerable<TDatatype>?>
    where TConverterType : JsonConverter
{
    public override bool HandleNull => true;

    // Cache the derived options (source options with only TConverterType as converter) per source options
    // instance, so Read/Write don't clone JsonSerializerOptions + Activator.CreateInstance on every call.
    private static readonly ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> _ItemOptions = [];

    public override IEnumerable<TDatatype>? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        var serializerOptions = _ItemOptions.GetValue(options, _CreateItemOptions);

        List<TDatatype> returnValue = [];

        while (reader.TokenType is not JsonTokenType.EndArray)
        {
            if (reader.TokenType is not JsonTokenType.StartArray)
            {
                returnValue.Add(JsonSerializer.Deserialize<TDatatype>(ref reader, serializerOptions)!);
            }

            reader.Read();
        }

        return returnValue;
    }

    public override void Write(Utf8JsonWriter writer, IEnumerable<TDatatype>? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();

            return;
        }

        var serializerOptions = _ItemOptions.GetValue(options, _CreateItemOptions);

        writer.WriteStartArray();

        foreach (var data in value)
        {
            JsonSerializer.Serialize(writer, data, serializerOptions);
        }

        writer.WriteEndArray();
    }

    private static JsonSerializerOptions _CreateItemOptions(JsonSerializerOptions source)
    {
        var clone = new JsonSerializerOptions(source);
        clone.Converters.Clear();
        clone.Converters.Add(Activator.CreateInstance<TConverterType>());

        return clone;
    }
}
