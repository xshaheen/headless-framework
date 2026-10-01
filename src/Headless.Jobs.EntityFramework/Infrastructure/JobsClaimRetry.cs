// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sql;
using Headless.Threading;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.Infrastructure;

/// <summary>
/// Retries a native claim scope that failed with a transient fault before its commit started, on the rule
/// <see cref="SqlAutonomousTransaction.RetryAsync{T}" /> applies to every autonomous store call. Each attempt opens
/// its own <see cref="JobsClaimTransaction{TDbContext}" />, so a retry starts clean and keeps the root/descendant and
/// definition/occurrence atomicity boundaries; claims never run inside a caller's transaction, so retrying one is
/// safe. A fault from the commit is never retried: the claim may already be durable.
/// </summary>
internal static partial class JobsClaimRetry
{
    /// <summary>Runs <paramref name="scope" />, retrying a transient fault raised before its commit.</summary>
    public static async Task<TResult> RunAsync<TResult>(
        Func<SqlAutonomousAttempt, CancellationToken, Task<TResult>> scope,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        return await SqlAutonomousTransaction
            .RetryAsync(
                scope,
                timeProvider,
                (ex, attemptNumber) => LogClaimRetry(logger, attemptNumber, TransientRetry.MaxAttempts, ex),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    [LoggerMessage(
        EventId = 20102,
        EventName = "JobsClaimTransientRetry",
        Level = LogLevel.Warning,
        Message = "Jobs claim failed with a transient fault before its commit; retrying attempt {AttemptNumber}/{MaxAttempts}."
    )]
    private static partial void LogClaimRetry(ILogger logger, int attemptNumber, int maxAttempts, Exception exception);
}
