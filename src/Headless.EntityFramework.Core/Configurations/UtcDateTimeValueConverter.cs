// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Headless.EntityFramework.Configurations;

/// <summary>
/// Converts <see cref="DateTime"/> values to UTC on both the database write and read paths.
/// </summary>
/// <remarks>
/// <see cref="DateTimeKind.Unspecified"/> values are treated as values that are already UTC and are stamped without
/// shifting their clock value. This matches the values returned by relational database providers.
/// </remarks>
[PublicAPI]
public sealed class UtcDateTimeValueConverter(ConverterMappingHints? mappingHints = null)
    : ValueConverter<DateTime, DateTime>(
        value => value.NormalizeToUtc(),
        value => value.NormalizeToUtc(),
        mappingHints
    );

/// <summary>
/// Converts nullable <see cref="DateTime"/> values to UTC on both the database write and read paths.
/// </summary>
/// <remarks>
/// <see langword="null"/> values remain unchanged. <see cref="DateTimeKind.Unspecified"/> values are treated as values
/// that are already UTC and are stamped without shifting their clock value.
/// </remarks>
[PublicAPI]
public sealed class NullableUtcDateTimeValueConverter(ConverterMappingHints? mappingHints = null)
    : ValueConverter<DateTime?, DateTime?>(
        value => value.HasValue ? value.Value.NormalizeToUtc() : value,
        value => value.HasValue ? value.Value.NormalizeToUtc() : value,
        mappingHints
    );
