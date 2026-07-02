// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Shared helpers used by both the mutex (<see cref="DistributedLock"/>) and the
/// reader-writer (<see cref="DistributedReadWriteLock"/>) providers. Kept internal
/// because the behavior is private contract between the two providers — callers should not depend
/// on these signatures.
/// </summary>
internal static class DistributedLockCoreHelpers
{
    private static readonly TimeSpan _MinRetryDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan _MaxRetryDelay = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Suffix appended to a writer's lock id to derive the writer-waiting marker placed in storage
    /// while readers drain (writer-preference; see D8). Shared by both .NET storage layers and
    /// embedded inline in the matching Lua scripts so any future change keeps the two surfaces in
    /// lockstep.
    /// </summary>
    internal const string WriterWaitingSuffix = ":_WRITERWAITING";

    /// <summary>
    /// Deterministic derivation of the writer-waiting marker id from a writer's lock id. Pure;
    /// the same input always produces the same output so cleanup-or-release round trips agree
    /// with the original acquire-time marker.
    /// </summary>
    [Pure]
    public static string GetWriterWaitingId(string leaseId)
    {
        return leaseId + WriterWaitingSuffix;
    }

    /// <summary>
    /// Normalizes the lease duration: <see langword="null"/> falls back to <paramref name="defaultTimeUntilExpires"/>,
    /// <see cref="Timeout.InfiniteTimeSpan"/> is translated to <see langword="null"/> (no expiration), and
    /// any finite value is validated as positive and capped below <see cref="int.MaxValue"/> milliseconds.
    /// </summary>
    /// <remarks>
    /// The upper bound of <see cref="int.MaxValue"/> milliseconds (~24.8 days) reflects the wire
    /// format: lease TTLs travel to storage providers as <see langword="int"/> millisecond values
    /// (Redis PX, Lua ARGV). A larger value would silently overflow on cast and plant a corrupted
    /// TTL. Rejecting at validation time surfaces the misuse as <see cref="ArgumentException"/>.
    /// </remarks>
    [Pure]
    public static TimeSpan? NormalizeTimeUntilExpires(TimeSpan? timeUntilExpires, TimeSpan defaultTimeUntilExpires)
    {
        if (timeUntilExpires is null)
        {
            return defaultTimeUntilExpires;
        }

        if (timeUntilExpires == Timeout.InfiniteTimeSpan)
        {
            return null;
        }

        var value = Argument.IsPositive(timeUntilExpires.Value);
        Argument.IsLessThan(value.TotalMilliseconds, int.MaxValue, paramName: nameof(timeUntilExpires));

        return value;
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
    /// <see cref="Timeout.InfiniteTimeSpan"/> (wait forever); rejects other negatives or extremely large values.
    /// </summary>
    public static void ValidateAcquireTimeout(TimeSpan? acquireTimeout)
    {
        if (acquireTimeout is null || acquireTimeout == Timeout.InfiniteTimeSpan)
        {
            return;
        }

        var value = Argument.IsPositiveOrZero(acquireTimeout.Value, paramName: nameof(acquireTimeout));
        Argument.IsLessThan(value.TotalMilliseconds, int.MaxValue, paramName: nameof(acquireTimeout));
    }

    /// <summary>
    /// Exponential backoff with jitter: 50ms, 100ms, 200ms, ..., capped at 3s, with ±25% jitter to
    /// avoid a thundering herd of waiters retrying in lockstep.
    /// </summary>
    /// <remarks>
    /// Not <see cref="PureAttribute"/>: this method advances <see cref="Random.Shared"/>'s PRNG
    /// state when sampling the jitter, so two consecutive calls with the same <paramref name="attempt"/>
    /// return different values. Treating the result as "ignorable" would mask a real bug.
    /// </remarks>
    public static TimeSpan GetBackoffDelay(int attempt)
    {
        var delayMs = _MinRetryDelay.TotalMilliseconds * (1 << Math.Min(attempt, 6));
        var cappedDelayMs = Math.Min(delayMs, _MaxRetryDelay.TotalMilliseconds);
#pragma warning disable CA5394 // Non-security jitter for retry backoff; cryptographic RNG is unnecessary here.
        var jitter = cappedDelayMs * ((Random.Shared.NextDouble() * 0.5) - 0.25);
#pragma warning restore CA5394

        return TimeSpan.FromMilliseconds(cappedDelayMs + jitter);
    }

    /// <summary>
    /// Configures the outbox bus reference used by the providers. Logs once when no bus
    /// is registered so operators see why waiters fall back to polling.
    /// </summary>
    public static IOutboxBus? ConfigureOutboxBus(IOutboxBus? outboxBus, ILogger logger)
    {
        if (outboxBus is null)
        {
            logger.LogOutboxBusAbsent();
        }

        return outboxBus;
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
    /// <c>_MaxReleaseRetryAttempts</c> in <see cref="DistributedLock"/>. Shared with
    /// <see cref="DistributedReadWriteLock"/>'s release path so both flows get the
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
