// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Headless.Jobs.Configurations;

internal sealed class JobsUtcDateTimeValueConverter()
    : ValueConverter<DateTime, DateTime>(
        value => JobsUtcDateTimeNormalizer.Normalize(value),
        value => JobsUtcDateTimeNormalizer.Normalize(value)
    );

internal sealed class JobsNullableUtcDateTimeValueConverter()
    : ValueConverter<DateTime?, DateTime?>(
        value => value.HasValue ? JobsUtcDateTimeNormalizer.Normalize(value.Value) : value,
        value => value.HasValue ? JobsUtcDateTimeNormalizer.Normalize(value.Value) : value
    );

internal static class JobsUtcDateTimeNormalizer
{
    public static DateTime Normalize(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
