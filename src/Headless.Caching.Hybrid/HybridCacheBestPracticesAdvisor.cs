// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Headless.Caching;

/// <summary>
/// Runs once at host startup and logs <see cref="LogLevel.Warning"/> messages for
/// <see cref="HybridCacheOptions"/> configurations that are valid but likely unintentional.
/// Never throws — advisory only.
/// </summary>
internal sealed partial class HybridCacheBestPracticesAdvisor(
    HybridCacheOptions options,
    ILogger<HybridCacheBestPracticesAdvisor> logger,
    bool invalidationConsumerRegistered,
    string? instanceName = null
) : IHostedLifecycleService
{
    // AutoRecoveryDelay above this threshold produces a graveyard-sized replay lag.
    private static readonly TimeSpan _AutoRecoveryDelayThreshold = TimeSpan.FromMinutes(5);

    // EagerRefreshThreshold at or above this gives so little lead time it rarely fires before TTL.
    private const float _EagerRefreshThresholdLimit = 0.95f;

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        // Named instances advise under a logging scope so an operator can tell which hybrid cache a warning is
        // about (the default/unnamed instance advises without the scope).
        using var scope = instanceName is null
            ? null
            : logger.BeginScope("Named hybrid cache instance {CacheInstanceName}", instanceName);

        _Advise(options);
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void _Advise(HybridCacheOptions o)
    {
        // Check 1 — auto-recovery is enabled but the replay cadence is very large.
        // A delay > 5 minutes means the recovery loop will sit idle for long stretches; by the time
        // a failed L2 write is replayed, the entry's TTL may already have elapsed on other nodes.
        if (o.EnableAutoRecovery && o.AutoRecoveryDelay > _AutoRecoveryDelayThreshold)
        {
            logger.LogAutoRecoveryDelayTooLarge(o.AutoRecoveryDelay, _AutoRecoveryDelayThreshold);
        }

        // Check 6 — auto-recovery is enabled but the distributed-cache circuit breaker is disabled (the default).
        // Auto-recovery replays failed L2 writes on a bounded background cadence, but without a breaker there is no
        // bypass window: every read that misses L1 still attempts L2 on each request during an outage. The L2 read
        // timeouts (DistributedCacheSoftTimeout/HardTimeout) bound each attempt's latency but do not stop the
        // repeated attempts, so a sustained L2 outage keeps hammering the down dependency.
        if (o.EnableAutoRecovery && o.DistributedCacheCircuitBreakerDuration == TimeSpan.Zero)
        {
            logger.LogAutoRecoveryWithoutCircuitBreaker();
        }

        // Check 5 — messaging backplane is wired (IBus is present) but no consumer for
        // CacheInvalidationMessage was registered. The hybrid cache publishes invalidations on every
        // write/remove, but without a consumer those messages are never received by any instance —
        // creating a silent one-way backplane where peers never evict their local L1 entries.
        if (!invalidationConsumerRegistered)
        {
            logger.LogInvalidationConsumerNotRegistered();
        }

        var entry = o.DefaultEntryOptions;

        if (entry is null)
        {
            return;
        }

        // Check 2 — fail-safe is on but FailSafeMaxDuration <= Duration, so the physical retention
        // window equals the logical freshness window. The coordinator applies max(Duration, FailSafeMaxDuration),
        // so when they are equal the entry expires physically at the same time it expires logically — there is
        // no graveyard reserve for fail-safe to serve.
        if (
            entry.Value.IsFailSafeEnabled
            && entry.Value.FailSafeMaxDuration <= entry.Value.Duration
            && entry.Value.Duration > TimeSpan.Zero
        )
        {
            logger.LogFailSafeMaxDurationNotBeyondDuration(entry.Value.FailSafeMaxDuration, entry.Value.Duration);
        }

        // Check 3 — FactorySoftTimeout is finite but fail-safe is disabled.
        // The soft timeout only has an effect when fail-safe is on and a stale reserve exists;
        // without fail-safe the coordinator ignores it and the entry behaves as if it were infinite.
        if (
            entry.Value.FactorySoftTimeout != Timeout.InfiniteTimeSpan
            && entry.Value.FactorySoftTimeout > TimeSpan.Zero
            && !entry.Value.IsFailSafeEnabled
        )
        {
            logger.LogFactorySoftTimeoutInertWithoutFailSafe(entry.Value.FactorySoftTimeout);
        }

        // Check 4 — EagerRefreshThreshold is set very close to 1.
        // At 0.95 the background refresh window is only the last 5 % of the TTL; for a 1-minute entry
        // that is 3 seconds — barely enough to matter. The feature is effectively a no-op.
        if (entry.Value.EagerRefreshThreshold >= _EagerRefreshThresholdLimit)
        {
            logger.LogEagerRefreshThresholdTooHigh(
                entry.Value.EagerRefreshThreshold.Value,
                _EagerRefreshThresholdLimit
            );
        }
    }
}

internal static partial class HybridCacheBestPracticesAdvisorLogger
{
    [LoggerMessage(
        EventId = 5,
        EventName = "InvalidationConsumerNotRegistered",
        Level = LogLevel.Warning,
        Message = "No consumer for CacheInvalidationMessage is registered. The hybrid cache publishes "
            + "invalidation messages on every write and remove, but without a consumer those messages are "
            + "never received — peers will never evict their local L1 entries (silent one-way backplane). "
            + "Register HybridCacheInvalidationConsumer with Headless messaging, for example: "
            + "services.ForMessage<CacheInvalidationMessage>(msg => msg.OnBus<HybridCacheInvalidationConsumer>())."
    )]
    public static partial void LogInvalidationConsumerNotRegistered(this ILogger logger);

    [LoggerMessage(
        EventId = 1,
        EventName = "AutoRecoveryDelayTooLarge",
        Level = LogLevel.Warning,
        Message = "HybridCacheOptions.AutoRecoveryDelay is {AutoRecoveryDelay}, which exceeds the recommended "
            + "maximum of {Threshold}. A large delay means failed L2 writes and invalidation publishes will "
            + "not be replayed for a long time; consider setting AutoRecoveryDelay to a value <= 5 minutes."
    )]
    public static partial void LogAutoRecoveryDelayTooLarge(
        this ILogger logger,
        TimeSpan autoRecoveryDelay,
        TimeSpan threshold
    );

    [LoggerMessage(
        EventId = 2,
        EventName = "FailSafeMaxDurationNotBeyondDuration",
        Level = LogLevel.Warning,
        Message = "HybridCacheOptions.DefaultEntryOptions has IsFailSafeEnabled=true but "
            + "FailSafeMaxDuration ({FailSafeMaxDuration}) <= Duration ({Duration}). "
            + "The coordinator retains the entry for max(Duration, FailSafeMaxDuration), so no graveyard "
            + "reserve exists beyond normal expiry and fail-safe can never serve a stale value. "
            + "Set FailSafeMaxDuration to a value greater than Duration to create a usable reserve."
    )]
    public static partial void LogFailSafeMaxDurationNotBeyondDuration(
        this ILogger logger,
        TimeSpan failSafeMaxDuration,
        TimeSpan duration
    );

    [LoggerMessage(
        EventId = 3,
        EventName = "FactorySoftTimeoutInertWithoutFailSafe",
        Level = LogLevel.Warning,
        Message = "HybridCacheOptions.DefaultEntryOptions has FactorySoftTimeout set to {FactorySoftTimeout} "
            + "but IsFailSafeEnabled=false. The soft timeout only takes effect when fail-safe is enabled and a "
            + "stale reserve is available; without fail-safe it is silently ignored. Either enable fail-safe "
            + "(set IsFailSafeEnabled=true and a FailSafeMaxDuration > Duration) or remove FactorySoftTimeout."
    )]
    public static partial void LogFactorySoftTimeoutInertWithoutFailSafe(
        this ILogger logger,
        TimeSpan factorySoftTimeout
    );

    [LoggerMessage(
        EventId = 4,
        EventName = "EagerRefreshThresholdTooHigh",
        Level = LogLevel.Warning,
        Message = "HybridCacheOptions.DefaultEntryOptions has EagerRefreshThreshold={EagerRefreshThreshold}, "
            + "which is >= {Limit}. The background refresh window (the remaining fraction of TTL after the "
            + "threshold) is so small that eager refresh rarely fires before natural expiry. "
            + "Consider setting EagerRefreshThreshold to a value below 0.90 to get a meaningful refresh window."
    )]
    public static partial void LogEagerRefreshThresholdTooHigh(
        this ILogger logger,
        float eagerRefreshThreshold,
        float limit
    );

    [LoggerMessage(
        EventId = 6,
        EventName = "AutoRecoveryWithoutCircuitBreaker",
        Level = LogLevel.Warning,
        Message = "HybridCacheOptions.EnableAutoRecovery is true but DistributedCacheCircuitBreakerDuration is zero "
            + "(the circuit breaker is disabled). Without a breaker there is no bypass window: every read that misses "
            + "L1 still attempts L2 on each request during an outage — the L2 read timeouts bound each attempt's "
            + "latency but not the repeated attempts — so a sustained L2 outage keeps hammering the down dependency. "
            + "Set DistributedCacheCircuitBreakerDuration to a non-zero value to pair the breaker's bypass window "
            + "with auto-recovery."
    )]
    public static partial void LogAutoRecoveryWithoutCircuitBreaker(this ILogger logger);
}
