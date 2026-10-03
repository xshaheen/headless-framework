// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization.Metadata;
using Headless.Primitives;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Headless.EntityFramework.Configurations;

/// <summary>
/// EF Core value converter that serializes a nullable <c>Locales</c> dictionary to a JSON string for
/// storage and deserializes it on read. A <see langword="null"/> or <c>{}</c> value round-trips to
/// <see langword="null"/>.
/// </summary>
[PublicAPI]
public sealed class LocalesValueConverter()
    : ValueConverter<Locales?, string?>(x => _Serialize(x), x => _Deserialize(x))
{
    private static readonly JsonTypeInfo<Locales> _TypeInfo = EfCoreJsonSerializerContext.Default.Locales;

    private static string _Serialize(Locales? locale)
    {
        return locale is null ? "{}" : JsonSerializer.Serialize(locale, _TypeInfo);
    }

    private static Locales? _Deserialize(string? json)
    {
        return string.IsNullOrEmpty(json) || string.Equals(json, "{}", StringComparison.Ordinal)
            ? null
            : JsonSerializer.Deserialize(json, _TypeInfo);
    }
}
