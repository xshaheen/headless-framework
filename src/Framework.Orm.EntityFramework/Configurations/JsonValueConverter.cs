// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization.Metadata;
using Framework.Serializer;
using Framework.Serializer.Converters;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Framework.Orm.EntityFramework.Configurations;

/// <summary>
/// Value converter that serializes/deserializes a property to/from JSON using reflection-based serialization.
/// </summary>
[PublicAPI]
public sealed class JsonValueConverter<TPropertyType>()
    : ValueConverter<TPropertyType, string>(d => _SerializeObject(d), s => _DeserializeObject(s))
{
    private static readonly JsonSerializerOptions _Options = _CreateJsonOptions();

    private static string _SerializeObject(TPropertyType d)
    {
        return JsonSerializer.Serialize(d, _Options);
    }

    private static TPropertyType _DeserializeObject(string s)
    {
        return JsonSerializer.Deserialize<TPropertyType>(s, _Options)!;
    }

    private static JsonSerializerOptions _CreateJsonOptions()
    {
        var option = new JsonSerializerOptions();

        JsonConstants.ConfigureInternalJsonOptions(option);
        option.Converters.Add(new ObjectToInferredTypesJsonConverter());

        return option;
    }
}

/// <summary>
/// Value converter that serializes/deserializes a property to/from JSON using source-generated metadata.
/// AOT/trimming compatible.
/// </summary>
[PublicAPI]
public sealed class JsonValueConverter<TPropertyType, TContext>(JsonTypeInfo<TPropertyType> typeInfo)
    : ValueConverter<TPropertyType, string>(
        d => JsonSerializer.Serialize(d, typeInfo),
        s => JsonSerializer.Deserialize(s, typeInfo)!
    )
    where TContext : JsonSerializerContext;
