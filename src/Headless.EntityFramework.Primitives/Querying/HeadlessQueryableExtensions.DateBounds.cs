// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Microsoft.EntityFrameworkCore;

public static partial class HeadlessQueryableExtensions
{
    /// <summary>
    /// The exclusive bounds of a month-count query. The predicate reads them from this captured object so EF Core
    /// extracts them as query parameters: inline constants would compile and cache a separate query for every range.
    /// </summary>
    private sealed class DateBounds<TDate>(TDate after, TDate before)
    {
        public TDate After { get; } = after;

        public TDate Before { get; } = before;
    }
}
