// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Collections;
using Framework.Primitives;
using Framework.Serializer;
using Framework.Serializer.Json.Converters;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Framework.Orm.EntityFramework.Configurations;

[PublicAPI]
public sealed class ExtraPropertiesValueConverter()
    : ValueConverter<ExtraProperties, string>(x => _Serialize(x), x => _Deserialize(x))
{
    private static readonly JsonSerializerOptions _Options = _CreateJsonOptions();

    private static string _Serialize(ExtraProperties? extraProperties)
    {
        return JsonSerializer.Serialize(extraProperties, _Options);
    }

    private static ExtraProperties _Deserialize(string? json)
    {
        return string.IsNullOrEmpty(json) || string.Equals(json, "{}", StringComparison.Ordinal)
            ? []
            : JsonSerializer.Deserialize<ExtraProperties>(json, _Options) ?? [];
    }

    private static JsonSerializerOptions _CreateJsonOptions()
    {
        var option = new JsonSerializerOptions();

        JsonConstants.ConfigureInternalJsonOptions(option);
        option.Converters.Add(new ObjectToInferredTypesConverter());

        return option;
    }
}

[PublicAPI]
public sealed class ExtraPropertiesValueComparer()
    : ValueComparer<ExtraProperties>(
        equalsExpression: (a, b) => _Equal(a, b),
        hashCodeExpression: dictionary => _HashCode(dictionary),
        snapshotExpression: d => new ExtraProperties(d)
    )
{
    private static readonly IEqualityComparer<KeyValuePair<string, object?>> _KeyValuePairComparer =
        ComparerFactory.Create<KeyValuePair<string, object?>>(
            (p1, p2) => string.Equals(p1.Key, p2.Key, StringComparison.Ordinal) && p1.Value == p2.Value,
            pair => HashCode.Combine(pair.Key, pair.Value)
        );

    private static bool _Equal(ExtraProperties? dictionary1, ExtraProperties? dictionary2)
    {
        return (dictionary1 is null && dictionary2 is null)
            || (
                dictionary1 is not null
                && dictionary2 is not null
                && dictionary1.SequenceEqual(dictionary2, _KeyValuePairComparer)
            );
    }

    private static int _HashCode(ExtraProperties dictionary)
    {
        return dictionary.Aggregate(0, (key, pair) => HashCode.Combine(key, _KeyValuePairComparer.GetHashCode(pair)));
    }
}
