// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.Middlewares;

internal static class RetryAfterSeconds
{
    /// <summary>Converts a retry delay to <c>Retry-After</c> delta-seconds.</summary>
    /// <remarks>
    /// Whole seconds, rounded up, and never zero: a zero or truncated value invites the client to retry before the
    /// budget has reopened.
    /// </remarks>
    public static int From(TimeSpan retryAfter) =>
        (int)Math.Clamp(Math.Ceiling(retryAfter.TotalSeconds), 1, int.MaxValue);
}
