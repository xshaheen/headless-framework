// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Serializer.Converters;

/// <summary>
/// Converts a JSON value that is either a single item or a JSON array into a <typeparamref name="TCollection"/>.
/// A bare object token is wrapped in a one-element collection on read; on write the collection is always
/// serialized as a JSON array.
/// </summary>
/// <remarks>
/// A <see langword="null"/> JSON token or an empty JSON string is deserialized as <see langword="null"/>.
/// </remarks>
/// <typeparam name="TCollection">The collection type to produce. Must have a public parameterless constructor.</typeparam>
/// <typeparam name="TItem">The element type of the collection.</typeparam>
[RequiresUnreferencedCode(
    "JSON serialization and deserialization might require types that cannot be statically analyzed."
)]
[RequiresDynamicCode("JSON serialization and deserialization might require runtime code generation.")]
public class SingleOrCollectionJsonConverter<TCollection, TItem> : JsonConverter<TCollection>
    where TCollection : class, ICollection<TItem?>, new()
{
    public override TCollection? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType is JsonTokenType.String)
        {
            var str = reader.GetString();

            if (string.IsNullOrEmpty(str))
            {
                return null;
            }
        }

        if (reader.TokenType is JsonTokenType.StartArray)
        {
            var list = new TCollection();

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    break;
                }

                list.Add(JsonSerializer.Deserialize<TItem>(ref reader, options));
            }

            return list;
        }

        var item = JsonSerializer.Deserialize<TItem?>(ref reader, options);

        return [item];
    }

    public override void Write(Utf8JsonWriter writer, TCollection value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();

        foreach (var item in value)
        {
            JsonSerializer.Serialize(writer, item, options);
        }

        writer.WriteEndArray();
    }
}

/// <summary>
/// <see cref="SingleOrCollectionJsonConverter{TCollection,TItem}"/> specialization that produces a
/// <see cref="List{T}"/>.
/// </summary>
/// <typeparam name="TItem">The list element type.</typeparam>
[RequiresUnreferencedCode(
    "JSON serialization and deserialization might require types that cannot be statically analyzed."
)]
[RequiresDynamicCode("JSON serialization and deserialization might require runtime code generation.")]
public sealed class SingleOrListJsonConverter<TItem> : SingleOrCollectionJsonConverter<List<TItem?>, TItem>;

/// <summary>
/// <see cref="SingleOrCollectionJsonConverter{TCollection,TItem}"/> specialization that produces a
/// <see cref="HashSet{T}"/>.
/// </summary>
/// <typeparam name="TItem">The set element type.</typeparam>
[RequiresUnreferencedCode(
    "JSON serialization and deserialization might require types that cannot be statically analyzed."
)]
[RequiresDynamicCode("JSON serialization and deserialization might require runtime code generation.")]
public sealed class SingleOrHashsetJsonConverter<TItem> : SingleOrCollectionJsonConverter<HashSet<TItem?>, TItem>;
