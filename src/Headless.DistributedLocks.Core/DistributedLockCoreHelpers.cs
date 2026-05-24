// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Shared helpers used by both the mutex (<see cref="DistributedLockProvider"/>) and the
/// reader-writer (<see cref="DistributedReaderWriterLockProvider"/>) providers. Kept internal
/// because the behavior is private contract between the two providers — callers should not depend
/// on these signatures.
/// </summary>
internal static class DistributedLockCoreHelpers
{
    private static readonly TimeSpan _MinRetryDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan _MaxRetryDelay = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Normalizes the lease duration: <see langword="null"/> falls back to <paramref name="defaultTimeUntilExpires"/>,
    /// <see cref="Timeout.InfiniteTimeSpan"/> is translated to <see langword="null"/> (no expiration), and
    /// any finite value is validated as positive.
    /// </summary>
    [Pure]
    public static TimeSpan? NormalizeTimeUntilExpires(TimeSpan? timeUntilExpires, TimeSpan defaultTimeUntilExpires)
    {
        return timeUntilExpires is null ? defaultTimeUntilExpires
            : timeUntilExpires == Timeout.InfiniteTimeSpan ? null
            : Argument.IsPositive(timeUntilExpires.Value);
    }

    /// <summary>
    /// When lease monitoring is enabled, the lease duration must be finite; rejects
    /// <see cref="Timeout.InfiniteTimeSpan"/> via <see cref="ArgumentException"/>.
    /// </summary>
    [Pure]
    public static TimeSpan RequireFiniteLeaseDuration(TimeSpan? timeUntilExpires, bool monitorLease)
    {
        if (timeUntilExpires is { } leaseDuration)
        {
            return leaseDuration;
        }

        if (monitorLease)
        {
            throw new ArgumentException(
                "Lease monitoring requires a finite timeUntilExpires; Timeout.InfiniteTimeSpan is not valid.",
                nameof(timeUntilExpires)
            );
        }

        return Timeout.InfiniteTimeSpan;
    }

    /// <summary>
    /// Validates the caller-supplied acquire timeout. Allows <see langword="null"/> (use default) and
    /// <see cref="Timeout.InfiniteTimeSpan"/> (wait forever); rejects other negatives.
    /// </summary>
    public static void ValidateAcquireTimeout(TimeSpan? acquireTimeout)
    {
        if (acquireTimeout is null || acquireTimeout == Timeout.InfiniteTimeSpan)
        {
            return;
        }

        Argument.IsPositiveOrZero(acquireTimeout.Value, paramName: nameof(acquireTimeout));
    }

    /// <summary>
    /// Exponential backoff with jitter: 50ms, 100ms, 200ms, ..., capped at 3s, with ±25% jitter to
    /// avoid a thundering herd of waiters retrying in lockstep.
    /// </summary>
    [Pure]
    public static TimeSpan GetBackoffDelay(int attempt)
    {
        var delayMs = _MinRetryDelay.TotalMilliseconds * (1 << Math.Min(attempt, 6));
        var cappedDelayMs = Math.Min(delayMs, _MaxRetryDelay.TotalMilliseconds);
        var jitter = cappedDelayMs * ((Random.Shared.NextDouble() * 0.5) - 0.25);

        return TimeSpan.FromMilliseconds(cappedDelayMs + jitter);
    }

    /// <summary>
    /// Configures the outbox publisher reference used by the providers. Logs once when no publisher
    /// is registered so operators see why waiters fall back to polling.
    /// </summary>
    public static IOutboxPublisher? ConfigureOutboxPublisher(IOutboxPublisher? outboxPublisher, ILogger logger)
    {
        if (outboxPublisher is null)
        {
            logger.LogOutboxPublisherAbsent();
        }

        return outboxPublisher;
    }

    /// <summary>
    /// Transient = anything that isn't a programmer error or caller-driven cancellation. Mirrors
    /// the catch filter used by the acquire loops in both providers.
    /// </summary>
    public static bool IsTransientStorageException(Exception ex)
    {
        return ex
            is not (
                OperationCanceledException
                or ObjectDisposedException
                or InvalidOperationException
                or ArgumentException
            );
    }

    /// <summary>
    /// Long-running pipeline for the release path (critical path: failure to release strands
    /// waiters until TTL expiry). 15 total attempts (1 + 14 retries) matches the prior
    /// <c>_MaxReleaseRetryAttempts</c> in <see cref="DistributedLockProvider"/>. Shared with
    /// <see cref="DistributedReaderWriterLockProvider"/>'s release path so both flows get the
    /// same retry budget and jitter.
    /// </summary>
    public static ResiliencePipeline BuildReleasePipeline(TimeProvider timeProvider, ILogger logger)
    {
        return new ResiliencePipelineBuilder { TimeProvider = timeProvider }
            .AddRetry(
                new RetryStrategyOptions
                {
                    ShouldHandle = static args => new ValueTask<bool>(
                        args.Outcome.Exception is { } ex && IsTransientStorageException(ex)
                    ),
                    MaxRetryAttempts = 14,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromMilliseconds(50),
                    MaxDelay = TimeSpan.FromSeconds(2),
                    UseJitter = true,
                    OnRetry = args =>
                    {
                        logger.LogLockStorageRetry(args.AttemptNumber + 1, args.RetryDelay, args.Outcome.Exception);

                        return default;
                    },
                }
            )
            .Build();
    }
}
