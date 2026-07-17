// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Headless.EntityFramework.Configurations;

/// <summary>
/// EF Core value converter that normalizes <see cref="DateTime"/> values to <see cref="DateTimeKind.Utc"/> on both
/// the store-write and store-read paths, so a persisted instant never depends on the ambient machine timezone.
/// </summary>
/// <remarks>
/// Normalization is a pure function of the value's <see cref="DateTime.Kind"/> — it does not read a clock. The read
/// path matters most: relational providers return <see cref="DateTimeKind.Unspecified"/>, which this converter
/// re-stamps as UTC in place (never converts), matching what the write path stored.
/// </remarks>
[PublicAPI]
public sealed class NormalizeDateTimeValueConverter(ConverterMappingHints? mappingHints = null)
    : ValueConverter<DateTime, DateTime>(x => x.NormalizeToUtc(), x => x.NormalizeToUtc(), mappingHints);

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
