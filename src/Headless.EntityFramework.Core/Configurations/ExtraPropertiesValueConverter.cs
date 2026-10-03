// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Collections;
using Headless.Primitives;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Headless.EntityFramework.Configurations;

/// <summary>
/// EF Core value converter that serializes <c>ExtraProperties</c> to a JSON string for storage and
/// deserializes it back on read. An empty or <c>{}</c> JSON value round-trips to an empty
/// <c>ExtraProperties</c> instance.
/// </summary>
[PublicAPI]
public sealed class ExtraPropertiesValueConverter()
    : ValueConverter<ExtraProperties, string>(x => _Serialize(x), x => _Deserialize(x))
{
    private static readonly JsonSerializerOptions _Options = EfCoreJsonOptions.Instance;

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
}
