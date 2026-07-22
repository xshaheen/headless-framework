// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.Caching;

/// <summary>
/// Two-tier hybrid cache combining a process-local L1 (<see cref="IInMemoryCache"/>) with a shared
/// distributed L2 (<see cref="IRemoteCache"/>), with cross-instance L1 invalidation via
/// <see cref="CacheInvalidationMessage"/> published over <c>IBus</c>.
/// </summary>
/// <remarks>
/// <para><b>Read path (GetOrAddAsync):</b></para>
/// <list type="number">
/// <item>L1 hit (fast, no network) → return.</item>
/// <item>L2 hit → populate L1 with the remaining L2 logical TTL capped at <see cref="HybridCacheOptions.DefaultLocalExpiration"/> → return.</item>
/// <item>Both miss → run factory (stampede-protected per-key), write L1 + L2, publish invalidation so peer L1s evict their stale copies.</item>
/// </list>
/// <para><b>Write/Invalidation path:</b></para>
/// <list type="number">
/// <item>Publish invalidation message first to narrow the stale-read window on peers.</item>
/// <item>Write or remove from L1 and L2.</item>
/// <item>Peers receive the message and drop their local copies.</item>
/// </list>
/// <para>
/// L2 faults degrade gracefully by default: reads fall back to L1 or a miss; factory-path L2 writes are
/// logged and dropped (or queued when <see cref="HybridCacheOptions.EnableAutoRecovery"/> is on).
/// Set <see cref="HybridCacheOptions.ReThrowDistributedCacheExceptions"/> to fail loud instead.
/// </para>
/// <para>
/// This class is <see cref="IAsyncDisposable"/>; dispose it to drain the auto-recovery queue and release
/// internal resources. DI-registered singletons are disposed automatically on host shutdown.
/// </para>
/// </remarks>
[PublicAPI]
public sealed partial class HybridCache(
    IInMemoryCache l1Cache,
    IRemoteCache l2Cache,
    IBus publisher,
    HybridCacheOptions cacheOptions,
    ILogger<HybridCache>? logger = null,
    TimeProvider? timeProvider = null,
    ICacheFactoryLockProvider? factoryLockProvider = null,
    CacheInstrumentationConfig? instrumentation = null,
    CacheEventsConfig? eventsConfig = null
) : ICache, IFactoryCacheStore, IBufferCache, IAsyncDisposable
{
    private readonly ILogger _logger = logger ?? NullLogger<HybridCache>.Instance;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly string _instanceId = cacheOptions.InstanceId ?? Guid.NewGuid().ToString("N");

    // Framework-owned cross-node invalidation-routing identity (HybridCacheOptions.InvalidationRoutingName), set
    // by named-instance registration and null for the default instance. Distinct from the public CacheName below,
    // which only feeds telemetry and cannot affect routing (a user override in setupAction is applied before
    // registration stamps InvalidationRoutingName, so it never wins here).
    private readonly string? _invalidationRoutingName = cacheOptions.InvalidationRoutingName;

    // Non-null cache name for the headless.cache.name telemetry dimension (the nullable _invalidationRoutingName
    // above is used for invalidation routing and must stay null for the default instance).
    private readonly string _metricCacheName = string.IsNullOrEmpty(cacheOptions.CacheName)
        ? CachingDiagnostics.DefaultCacheName
        : cacheOptions.CacheName;

    private readonly FactoryCacheCoordinator _coordinator = new(
        timeProvider ?? TimeProvider.System,
        logger,
        factoryLockProvider,
        string.IsNullOrEmpty(cacheOptions.CacheName) ? CachingDiagnostics.DefaultCacheName : cacheOptions.CacheName,
        CachingMetrics.TierHybrid,
        instrumentation?.IncludeKeyInTraces ?? false,
        eventsConfig
    );

    private long _localCacheHits;
    private long _invalidateCacheCalls;
    private int _isDisposed;

    /// <inheritdoc />
    public CacheEntryOptions? DefaultEntryOptions { get; } = cacheOptions.DefaultEntryOptions;

    /// <inheritdoc />
    public ICacheEvents Events => _coordinator.EventsHub;

    /// <summary>Gets the number of L1 cache hits.</summary>
    public long LocalCacheHits => Interlocked.Read(ref _localCacheHits);

    /// <summary>Gets the number of invalidation calls received from other instances.</summary>
    public long InvalidateCacheCalls => Interlocked.Read(ref _invalidateCacheCalls);

    /// <summary>Provides direct access to the L1 (in-memory) cache for advanced scenarios.</summary>
    public IInMemoryCache LocalCache { get; } = l1Cache;

    /// <summary>The auto-recovery queue, when <see cref="HybridCacheOptions.EnableAutoRecovery"/> is set.</summary>
    /// <remarks>
    /// The queue owns its own TimeProvider timer so it works for any HybridCache lifetime (default DI
    /// singleton, named keyed instances, or direct construction) and is torn down in DisposeAsync.
    /// </remarks>
    internal HybridCacheRecoveryQueue? RecoveryQueue { get; } =
        cacheOptions.EnableAutoRecovery
            ? new HybridCacheRecoveryQueue(
                cacheOptions,
                timeProvider ?? TimeProvider.System,
                logger ?? NullLogger<HybridCache>.Instance
            )
            : null;

    /// <summary>
    /// Handles incoming cache invalidation message from other instances.
    /// Called by <see cref="HybridCacheInvalidationConsumer"/>.
    /// </summary>
    internal async ValueTask HandleInvalidationAsync(CacheInvalidationMessage message, CancellationToken ct)
    {
        // Skip self-originated messages
        if (string.Equals(message.InstanceId, _instanceId, StringComparison.Ordinal))
        {
            return;
        }

        // Conflict check before applying the invalidation: queued recovery items older than this message lost
        // the race to another node; replaying them would resurrect stale data.
        RecoveryQueue?.OnIncomingInvalidation(message);

        _logger.LogInvalidatingLocalCacheFromRemote(
            message.InstanceId,
            message.Keys?.Length ?? (message.Key is not null ? 1 : 0),
            message.Prefix is not null,
            message.FlushAll
        );

        Interlocked.Increment(ref _invalidateCacheCalls);

        if (message.FlushAll)
        {
            _logger.LogFlushedLocalCache();
            CachingMetrics.RecordInvalidation(
                _metricCacheName,
                CachingMetrics.InvalidationFlush,
                CachingMetrics.DirectionReceive
            );
            // Seed the L2 provider's remove-generation marker from the origin timestamp FIRST, then wipe L1. The
            // order matters: if L1 were wiped first, a concurrent read in the window before the L2 marker is seeded
            // would miss the wiped L1 and fall through to L2 (marker not yet seeded), serving the stale entry. The
            // remove marker only invalidates entries older than the flush timestamp, so seeding it before the wipe
            // cannot hide a still-valid entry. Providers whose flush is physical implement SeedRemoveMarker as a
            // no-op (FusionCache's payload-carrying backplane — the timestamp travels with the notification).
            if (l2Cache is ISeedableTagMarkerCache l2Markers)
            {
                l2Markers.SeedRemoveMarker(message.Timestamp ?? _timeProvider.GetUtcNow());
            }

            // Physically wipe this receiver's L1 (drops its local fail-safe reserves, matching the originator).
            await LocalCache.FlushAsync(ct).ConfigureAwait(false);

            return;
        }

        if (message.Clear)
        {
            var clearAt = message.Timestamp ?? _timeProvider.GetUtcNow();
            CachingMetrics.RecordInvalidation(
                _metricCacheName,
                CachingMetrics.InvalidationClear,
                CachingMetrics.DirectionReceive
            );

            // Seed the L1 clear-generation marker from the ORIGINATOR's timestamp (raise-only), not via ClearAsync
            // which would stamp the receiver's own clock: under cross-node clock skew a receiver lagging the origin
            // would write a marker older than a freshly-born local entry and fail to invalidate it. Reserves are
            // preserved (unlike FlushAll). Fall back to a local-clock ClearAsync only when L1 cannot be seeded.
            if (LocalCache is ISeedableTagMarkerCache l1Markers)
            {
                l1Markers.SeedClearMarker(clearAt);
            }
            else
            {
                await LocalCache.ClearAsync(ct).ConfigureAwait(false);
            }

            // Seed the L2 provider's process-local marker cache too (when it caches markers): without this, an
            // L1-miss read on this peer would fall through to L2 and observe the clear only after L2's refresh
            // window. The notification carries the timestamp, so no L2 round-trip is needed (FusionCache pattern).
            if (l2Cache is ISeedableTagMarkerCache l2Markers)
            {
                l2Markers.SeedClearMarker(clearAt);
            }

            return;
        }

        if (!string.IsNullOrEmpty(message.Prefix))
        {
            await LocalCache.RemoveByPrefixAsync(message.Prefix, ct).ConfigureAwait(false);
            return;
        }

        if (!string.IsNullOrEmpty(message.Tag))
        {
            var tagAt = message.Timestamp ?? _timeProvider.GetUtcNow();
            CachingMetrics.RecordInvalidation(
                _metricCacheName,
                CachingMetrics.InvalidationTag,
                CachingMetrics.DirectionReceive
            );

            // Seed the L1 tag marker from the ORIGINATOR's timestamp (raise-only), not via RemoveByTagAsync which
            // stamps the receiver's local clock — under clock skew a lagging receiver would record a marker older
            // than the invalidated entries' CreatedAt and miss the invalidation. Family-2 logical invalidation is
            // version-pinned by entry birth time, so a pending recovery write that landed after this invalidation
            // carries a newer CreatedAt and is naturally not invalidated by the older marker. Fall back to a
            // local-clock RemoveByTagAsync only when L1 cannot be seeded.
            if (LocalCache is ISeedableTagMarkerCache l1Markers)
            {
                l1Markers.SeedTagMarker(message.Tag, tagAt);
            }
            else
            {
                await LocalCache.RemoveByTagAsync(message.Tag, ct).ConfigureAwait(false);
            }

            // Seed the L2 provider's process-local marker cache too so an L1-miss read on this peer observes the
            // invalidation immediately rather than after L2's refresh window. The notification carries the
            // timestamp, so no L2 round-trip is needed (FusionCache's payload-carrying-backplane optimization).
            if (l2Cache is ISeedableTagMarkerCache l2Markers)
            {
                l2Markers.SeedTagMarker(message.Tag, tagAt);
            }

            return;
        }

        if (message.Keys is { Length: > 0 })
        {
            var keys = message.Keys;

            if (RecoveryQueue is not null)
            {
                // A key with a surviving recovery item has local intent at least as new as this message (the
                // conflict pass above dropped everything older): wiping its L1 entry would discard the newer
                // local write and make the stamp-verified replay drop itself as obsolete.
                keys = Array.FindAll(keys, key => !_ShouldIgnoreInvalidationFor(key));
            }

            if (keys.Length > 0)
            {
                if (message.Expire)
                {
                    // Logical expiration preserves each peer's fail-safe reserve: expire per key rather than
                    // removing, since there is no bulk logical-expire on the local store.
                    foreach (var key in keys)
                    {
                        await LocalCache.ExpireAsync(key, ct).ConfigureAwait(false);
                    }
                }
                else
                {
                    await LocalCache.RemoveAllAsync(keys, ct).ConfigureAwait(false);
                }
            }

            return;
        }

        if (!string.IsNullOrEmpty(message.Key))
        {
            if (!_ShouldIgnoreInvalidationFor(message.Key))
            {
                if (message.Expire)
                {
                    await LocalCache.ExpireAsync(message.Key, ct).ConfigureAwait(false);
                }
                else
                {
                    await LocalCache.RemoveAsync(message.Key, ct).ConfigureAwait(false);
                }
            }

            return;
        }

        _logger.LogUnknownInvalidateCacheMessage();
    }

    #region Private Helpers

    private async ValueTask _PublishInvalidationAsync(
        CacheInvalidationMessage message,
        CancellationToken ct,
        bool queueOnFailure = false
    )
    {
        // Stamp the publish time so receivers can run the auto-recovery conflict check against it. Replayed
        // invalidations arrive pre-stamped with the original write time so receivers still order them correctly
        // against operations that happened between the original write and its replay.
        if (message.Timestamp is null)
        {
            message = message with { Timestamp = _timeProvider.GetUtcNow() };
        }

        if (!string.Equals(message.CacheName, _invalidationRoutingName, StringComparison.Ordinal))
        {
            message = message with { CacheName = _invalidationRoutingName };
        }

        try
        {
            await publisher.PublishAsync(message, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!FactoryCacheCoordinator.IsCallerCancellation(ex, ct))
        {
            // Publish failure is non-fatal: other instances may have stale L1 data
            // until their TTL expires. This is acceptable for eventual consistency.
            _logger.LogFailedToPublishCacheInvalidation(
                ex,
                message.Keys?.Length ?? (message.Key is not null ? 1 : 0),
                message.Prefix is not null,
                message.FlushAll
            );

            // Only single-key invalidations from the auto-recovery capture paths are queued for re-publish;
            // every other path keeps today's fire-and-forget behavior.
            if (queueOnFailure && RecoveryQueue is not null && message.Key is not null)
            {
                _QueuePublishRecovery(message);
            }

            // Fail loud after the log + recovery queueing: self-healing still happens (the queued item replays),
            // but the caller is also informed of the backplane outage. On detached background publish paths this
            // re-throw is observed and logged by the fire-and-forget fault net rather than surfacing to a caller.
            if (cacheOptions.ReThrowBackplaneExceptions)
            {
                throw;
            }
        }
    }

    private bool _ShouldIgnoreInvalidationFor(string key)
    {
        // Only consulted after the OnIncomingInvalidation conflict pass dropped every queued item older than
        // the message, so a surviving item means our pending local operation is at least as new: applying the
        // older foreign invalidation would wipe the newer local state it is about to replay.
        if (RecoveryQueue?.Contains(key) != true)
        {
            return false;
        }

        _logger.LogIgnoredStaleRemoteInvalidation(key);
        return true;
    }

    /// <summary>
    /// Detaches the supplied background L2 work as fire-and-forget and attaches an observe-faulted continuation
    /// so a fault can never become an unobserved task exception (host-crash safe on .NET's escalation policy).
    /// The work itself is expected to handle its own L2/publish failures (recovery routing or best-effort log);
    /// this continuation is the last-resort net for anything that still escapes — mirrors the coordinator's
    /// <c>_ObserveFaultedTask</c> pattern. The lambda must capture every value it needs (key, value, message)
    /// BEFORE calling this so it never touches disposal-racing state after the caller returns.
    /// </summary>
    private void _RunDetached(Func<Task> work, string key)
    {
        Task task;

        try
        {
            task = work();
        }
        catch (Exception exception)
        {
            // A synchronous throw before the first await still must not surface to the (already-returned) caller.
            _logger.LogBackgroundDistributedCacheOperationFailed(exception, key, exception.GetType().Name);
            return;
        }

        if (task.IsCompletedSuccessfully)
        {
            return;
        }

        _ = task.ContinueWith(
            (faulted, state) =>
            {
                var (logger, faultedKey) = ((ILogger, string))state!;
                logger.LogBackgroundDistributedCacheOperationFailed(
                    faulted.Exception!,
                    faultedKey,
                    faulted.Exception!.GetType().Name
                );
            },
            (_logger, key),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default
        );
    }

    private void _ThrowIfDisposed()
    {
        Ensure.NotDisposed(Volatile.Read(ref _isDisposed) == 1, this);
    }

    private DateTime _GetUtcNow()
    {
        return _timeProvider.GetUtcNow().UtcDateTime;
    }

    private static DateTime _Min(DateTime left, DateTime right)
    {
        return left <= right ? left : right;
    }

    private TimeSpan? _GetLocalExpiration(TimeSpan? expiration)
    {
        if (!cacheOptions.DefaultLocalExpiration.HasValue)
        {
            return expiration;
        }

        return expiration < cacheOptions.DefaultLocalExpiration.Value
            ? expiration
            : cacheOptions.DefaultLocalExpiration;
    }

    #endregion

    #region IAsyncDisposable

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
        {
            return;
        }

        _coordinator.Dispose();

        if (RecoveryQueue is not null)
        {
            // Cancel the timer before draining so no new ProcessAsync pass starts after we await the active one.
            RecoveryQueue.Dispose();
            await RecoveryQueue.DrainAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    #endregion
}
