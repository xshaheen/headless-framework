// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Headless.EntityFramework.Configurations;

/// <summary>
/// Value converter that serializes/deserializes a property to/from JSON using reflection-based serialization.
/// </summary>
[PublicAPI]
public sealed class JsonValueConverter<TPropertyType>()
    : ValueConverter<TPropertyType, string>(d => _SerializeObject(d), s => _DeserializeObject(s))
{
    private static readonly JsonSerializerOptions _Options = EfCoreJsonOptions.Instance;

    private static string _SerializeObject(TPropertyType d)
    {
        return JsonSerializer.Serialize(d, _Options);
    }

    private static TPropertyType _DeserializeObject(string s)
    {
        return JsonSerializer.Deserialize<TPropertyType>(s, _Options)!;
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
