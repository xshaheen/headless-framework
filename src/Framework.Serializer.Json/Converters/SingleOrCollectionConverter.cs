// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Framework.Serializer.Json.Converters;

public class SingleOrCollectionConverter<TCollection, TItem> : JsonConverter<TCollection>
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

public sealed class SingleOrListConverter<TItem> : SingleOrCollectionConverter<List<TItem?>, TItem>;

public sealed class SingleOrHashsetConverter<TItem> : SingleOrCollectionConverter<HashSet<TItem?>, TItem>;
