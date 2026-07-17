// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Headless.Jobs.Configurations;

internal sealed class JobsUtcDateTimeValueConverter()
    : ValueConverter<DateTime, DateTime>(value => _Normalize(value), value => _Normalize(value))
{
    private static DateTime _Normalize(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}

internal sealed class JobsNullableUtcDateTimeValueConverter()
    : ValueConverter<DateTime?, DateTime?>(
        value => value.HasValue ? _Normalize(value.Value) : value,
        value => value.HasValue ? _Normalize(value.Value) : value
    )
{
    private static DateTime _Normalize(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
