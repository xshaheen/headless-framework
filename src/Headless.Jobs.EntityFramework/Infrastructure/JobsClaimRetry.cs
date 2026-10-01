// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Threading;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs.Infrastructure;

/// <summary>
/// Retries a native claim scope that the database rolled back as a deadlock victim or serialization failure. Each
/// attempt opens its own <see cref="JobsClaimTransaction{TDbContext}" />, so a retry starts clean and keeps the
/// root/descendant and definition/occurrence atomicity boundaries; claims never run inside a caller's transaction, so
/// retrying one is safe.
/// </summary>
internal static partial class JobsClaimRetry
{
    /// <summary>Runs <paramref name="scope" />, retrying the failures <paramref name="isTransient" /> accepts.</summary>
    public static async Task<TResult> RunAsync<TResult>(
        Func<CancellationToken, Task<TResult>> scope,
        Func<Exception, bool> isTransient,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        var failedAttempts = 0;

        return await TransientRetry
            .RunAsync(
                ct => new ValueTask<TResult>(scope(ct)),
                ex =>
                {
                    if (!isTransient(ex))
                    {
                        return false;
                    }

                    // TransientRetry asks only when another attempt will run, so this logs once per retry.
                    failedAttempts++;
                    LogClaimRetry(logger, failedAttempts + 1, TransientRetry.MaxAttempts, ex);

                    return true;
                },
                timeProvider,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    [LoggerMessage(
        EventId = 20102,
        EventName = "JobsClaimDeadlockRetry",
        Level = LogLevel.Warning,
        Message = "Jobs claim was rolled back by a deadlock or serialization failure; retrying attempt {AttemptNumber}/{MaxAttempts}."
    )]
    private static partial void LogClaimRetry(ILogger logger, int attemptNumber, int maxAttempts, Exception exception);
}
