// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Headless.EntityFramework.Configurations;

/// <summary>
/// EF Core value converter that normalizes nullable <see cref="DateTime"/> values to <see cref="DateTimeKind.Utc"/>,
/// leaving <see langword="null"/> values untouched.
/// </summary>
[PublicAPI]
public sealed class NullableNormalizeDateTimeValueConverter(ConverterMappingHints? mappingHints = null)
    : ValueConverter<DateTime?, DateTime?>(
        x => x.HasValue ? x.Value.NormalizeToUtc() : x,
        x => x.HasValue ? x.Value.NormalizeToUtc() : x,
        mappingHints
    );
