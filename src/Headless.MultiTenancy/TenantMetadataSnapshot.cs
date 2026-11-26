// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Headless.Primitives;

namespace Headless.MultiTenancy;

internal static class TenantMetadataSnapshot
{
    public static ExtraProperties Copy(ExtraProperties source)
    {
        var result = new ExtraProperties();

        foreach (var (key, value) in source)
        {
            // Holding nested values as immutable JSON lets later reads copy the bag without retaining
            // aliases to caller-owned objects or depending on a caller-owned JsonDocument's lifetime.
            result[key] = value switch
            {
                null
                or string
                or bool
                or char
                or byte
                or sbyte
                or short
                or ushort
                or int
                or uint
                or long
                or ulong
                or decimal
                or DateTime
                or DateTimeOffset
                or DateOnly
                or TimeOnly
                or TimeSpan
                or Guid
                or Enum => value,
                float number when float.IsFinite(number) => value,
                double number when double.IsFinite(number) => value,
                JsonElement element when element.ValueKind != JsonValueKind.Undefined => element.Clone(),
                _ => JsonSerializer.SerializeToElement(value),
            };
        }

        return result;
    }
}
