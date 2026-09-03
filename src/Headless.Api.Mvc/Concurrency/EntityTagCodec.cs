// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Net.Http.Headers;

namespace Headless.Api.Concurrency;

internal static class EntityTagCodec
{
    public static string Format(ReadOnlySpan<byte> value) => $"\"{Convert.ToBase64String(value)}\"";

    public static bool TryParseStrong(string? value, out byte[] etag)
    {
        etag = [];
        if (
            string.IsNullOrWhiteSpace(value)
            || !EntityTagHeaderValue.TryParseList([value], out var values)
            || values is null
            || values.Count != 1
        )
        {
            return false;
        }

        var parsed = values[0];
        var tag = parsed.Tag.Value;
        if (parsed.IsWeak || string.IsNullOrEmpty(tag) || string.Equals(tag, "*", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            etag = Convert.FromBase64String(tag.Trim('"'));
            return etag.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
