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
    // Shared by every provider's claim strategy, so one claim reports under one operation whatever the database.
    public const string ClaimTimeJobs = "jobs.claim_time_jobs";
    public const string ClaimTimedOutTimeJobs = "jobs.claim_timed_out_time_jobs";
    public const string ClaimCronJobOccurrences = "jobs.claim_cron_job_occurrences";
    public const string ClaimTimedOutCronJobOccurrences = "jobs.claim_timed_out_cron_job_occurrences";

    /// <summary>Runs <paramref name="scope" />, retrying a transient fault raised before its commit.</summary>
    public static async Task<TResult> RunAsync<TResult>(
        string operation,
        Func<SqlAutonomousAttempt, CancellationToken, Task<TResult>> scope,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        return await SqlAutonomousTransaction
            .RetryAsync(
                operation,
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
