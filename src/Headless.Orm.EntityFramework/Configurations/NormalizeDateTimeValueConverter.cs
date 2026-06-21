// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Headless.EntityFramework.Configurations;

/// <summary>
/// EF Core value converter that normalizes <see cref="DateTime"/> values through <c>IClock.Normalize</c>
/// on both the store-write and store-read paths, ensuring a consistent kind (for example UTC) is
/// stored and returned.
/// </summary>
[PublicAPI]
public sealed class NormalizeDateTimeValueConverter(IClock clock, ConverterMappingHints? mappingHints = null)
    : ValueConverter<DateTime, DateTime>(x => clock.Normalize(x), x => clock.Normalize(x), mappingHints);

/// <summary>
/// EF Core value converter that normalizes nullable <see cref="DateTime"/> values through
/// <c>IClock.Normalize</c>, leaving <see langword="null"/> values untouched.
/// </summary>
[PublicAPI]
public sealed class NullableNormalizeDateTimeValueConverter(IClock clock, ConverterMappingHints? mappingHints = null)
    : ValueConverter<DateTime?, DateTime?>(
        x => x.HasValue ? clock.Normalize(x.Value) : x,
        x => x.HasValue ? clock.Normalize(x.Value) : x,
        mappingHints
    );
