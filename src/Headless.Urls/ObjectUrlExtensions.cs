// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Urls;

/// <summary>
/// Internal copy of the <c>Headless.Extensions</c> invariant-string helper used by the URL builder. Duplicated here
/// (rather than referenced) so the dependency direction stays <c>Headless.Extensions → Headless.Urls</c> and never the
/// reverse. Like the rest of this package the helper is derived from Flurl (see THIRD-PARTY-NOTICES.md).
/// </summary>
internal static class ObjectUrlExtensions
{
    /// <summary>Returns a culture-invariant string representation of <paramref name="obj"/>.</summary>
    [return: NotNullIfNotNull(nameof(obj))]
    public static string? ToInvariantString(this object? obj)
    {
        // Taken from Flurl, which inspired by: http://stackoverflow.com/a/19570016/62600
        return obj switch
        {
            null => null,
            DateTime dt => dt.ToString(format: "o", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString(format: "o", CultureInfo.InvariantCulture),
            IConvertible c => c.ToString(CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(format: null, CultureInfo.InvariantCulture),
            _ => obj.ToString(),
        };
    }
}
