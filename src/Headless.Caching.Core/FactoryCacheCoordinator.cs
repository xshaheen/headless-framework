// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Headless.Checks;
using Headless.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Caching;

/// <summary>Coordinates factory-backed cache operations across cache providers.</summary>
[PublicAPI]
public sealed partial class FactoryCacheCoordinator(
    TimeProvider timeProvider,
    ILogger? logger = null,
    ICacheFactoryLockProvider? factoryLockProvider = null
) : IDisposable
{
    private readonly TimeProvider _timeProvider = Argument.IsNotNull(timeProvider);
    private readonly KeyedAsyncLock _keyedLock = new();
    private readonly ILogger _logger = logger ?? NullLogger<FactoryCacheCoordinator>.Instance;
    private const int _MaxInertWarningKeys = 1024;

    // Re-emit the "cap reached" notice once every this many post-cap suppressed occurrences so the misconfiguration
    // stays observable on a long-lived coordinator instead of being logged exactly once and then going silent.
    private const int _InertCapReLogInterval = 1024;
    private readonly ConcurrentDictionary<string, byte> _softTimeoutInertWarnings = new(StringComparer.Ordinal);

    // Counts inert keys suppressed after the per-key warning set hit _MaxInertWarningKeys. Drives the periodic
    // cap-reached re-log (every _InertCapReLogInterval). Interlocked-incremented on the timeout-selection hot path.
    private long _softTimeoutInertCapSuppressed;

    /// <summary>Signals test code that a detached background completion has registered its ceiling timer.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal Action? BackgroundCompletionCeilingTimerRegistered { get; set; }

    /// <summary>Signals test code that a foreground factory timeout timer has been registered.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal Action? FactoryTimeoutTimerRegistered { get; set; }

    /// <summary>Signals test code that a detached background operation (soft-timeout completion or eager refresh) has finished or abandoned its factory.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal Action? BackgroundOperationFinished { get; set; }

    /// <summary>Gets or creates a cache value by using the provider store primitive.</summary>
    /// <remarks>
    /// This overload adapts the simple value factory onto the conditional-factory engine
    /// (<see cref="GetOrAddAsync{T}(IFactoryCacheStore, string, Func{CacheFactoryContext{T}, CancellationToken, ValueTask{CacheFactoryResult{T}}}, CacheEntryOptions, CancellationToken)"/>),
    /// so both overloads share one state machine with identical timeout, fail-safe, and refresh semantics.
    /// </remarks>
    /// <typeparam name="T">The cached value type.</typeparam>
    /// <param name="store">The provider store.</param>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">The value factory.</param>
    /// <param name="options">The cache entry options.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        IFactoryCacheStore store,
        string key,
        Func<CancellationToken, ValueTask<T?>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(factory);

        // Wrap the simple factory in a struct adapter whose instance method becomes the delegate target. The
        // struct is boxed once when the delegate is formed, REPLACING the compiler-generated display class a
        // capturing lambda would allocate — the heap-object count is unchanged, but the shape is explicit and
        // carries no captured-variable lifetime hazards. (A truly zero-alloc fast path would require inlining
        // the L1-hit check here before forming any delegate; not done.)
        var adapter = new SimpleFactoryAdapter<T>(factory);
        return GetOrAddAsync<T>(store, key, adapter.InvokeAsync, options, cancellationToken);
    }

    /// <summary>
    /// Gets or refreshes a cache value using a conditional factory (the HTTP-304 pattern). The factory receives a
    /// <see cref="CacheFactoryContext{T}"/> carrying the last-known cached value and its validators and returns
    /// <see cref="CacheFactoryContext{T}.NotModified"/> to re-stamp the existing entry as fresh, or
    /// <see cref="CacheFactoryContext{T}.Modified(T, string?, DateTime?)"/> to replace it. The factory may also
    /// replace <see cref="CacheFactoryContext{T}.Options"/> before returning (adaptive caching); the replacement
    /// is re-validated before the write and an invalid mutation throws after the factory has run.
    /// </summary>
    /// <typeparam name="T">The cached value type.</typeparam>
    /// <param name="store">The provider store.</param>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">The conditional value factory; receives the per-execution context and the cancellation token.</param>
    /// <param name="options">The cache entry options; the factory may replace them via the context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask<CacheValue<T>> GetOrAddAsync<T>(
        IFactoryCacheStore store,
        string key,
        Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheEntryOptions options,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(store);
        Argument.IsNotNullOrEmpty(key);
        Argument.IsNotNull(factory);
        _ValidateOptions(options);

        if (options.UseDistributedFactoryLock && factoryLockProvider is null)
        {
            throw new InvalidOperationException(
                $"{nameof(CacheEntryOptions)}.{nameof(CacheEntryOptions.UseDistributedFactoryLock)} is enabled for "
                    + $"cache key '{key}' but no {nameof(ICacheFactoryLockProvider)} is registered. Reference the "
                    + "Headless.Caching.DistributedLocks adapter package and call "
                    + "setup.UseDistributedFactoryLock() inside AddHeadlessCaching, or disable the option."
            );
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Per-tier read control: skip reading L1 and/or L2 on a multi-tier (hybrid) store. Setting both is a miss,
        // matching SkipCacheRead; single-tier providers ignore the flags. Derived once and threaded through every
        // store read of this operation (pre-lock, under-lock, post-distributed-lock, and eager-refresh double-check)
        // so all reads share one tier policy.
        var readOptions = FactoryCacheReadOptions.FromEntryOptions(options);

        // Force-refresh (SkipCacheRead): bypass every store read and go straight to the factory. NotFound makes
        // the freshness/stale checks below false, so the read short-circuits and the eager/sliding read-path
        // helpers never fire; the per-key lock and any distributed lease are still acquired/released normally,
        // and staleCandidate stays NotFound so fail-safe cannot serve a reserve (none was read).
        var entry = options.SkipCacheRead
            ? CacheStoreEntry<T>.NotFound
            : await _TryGetEntryAsync<T>(store, key, readOptions, cancellationToken).ConfigureAwait(false);
        var now = _GetUtcNow();

        if (entry.IsFresh(now))
        {
            await _TryRearmSlidingEntryAsync(store, key, entry, now).ConfigureAwait(false);
            _MaybeStartEagerRefresh(store, key, factory, options, entry, now);
            return _ToCacheValue(entry, isStale: false);
        }

        if (_ShouldServeStaleImmediately(options, entry, now))
        {
            return _ToCacheValue(entry, isStale: true);
        }

        var staleCandidate = _IsStaleCandidate(entry, now) ? entry : CacheStoreEntry<T>.NotFound;
        var lockTimeout = _SelectLockTimeout(options, staleCandidate, now);
        var releaser = await _keyedLock
            .LockAsync(key, lockTimeout, _timeProvider, cancellationToken)
            .ConfigureAwait(false);

        if (releaser is null)
        {
            // With a stale reserve the wait is the soft timeout and we serve stale; without one it is LockTimeout
            // and _ToCacheValue(NotFound) degrades to a miss (NoValue).
            _logger.LogCacheFactoryTimedOut(key, staleCandidate.Found ? "lock-soft" : "lock-timeout", lockTimeout);
            return _ToCacheValue(staleCandidate, isStale: true);
        }

        var ownsReleaser = true;
        IAsyncDisposable? distributedLease = null;

        try
        {
            // Force-refresh skips the under-lock re-check too: there is no cached value to honor, so go straight
            // to the factory while still holding the per-key lock for single-flight.
            if (!options.SkipCacheRead)
            {
                entry = await _TryGetEntryAsync<T>(store, key, readOptions, cancellationToken).ConfigureAwait(false);
                now = _GetUtcNow();

                if (entry.IsFresh(now))
                {
                    await _TryRearmSlidingEntryAsync(store, key, entry, now).ConfigureAwait(false);
                    return _ToCacheValue(entry, isStale: false);
                }

                if (_ShouldServeStaleImmediately(options, entry, now))
                {
                    return _ToCacheValue(entry, isStale: true);
                }

                if (_IsStaleCandidate(entry, now))
                {
                    staleCandidate = entry;
                }
            }

            if (options.UseDistributedFactoryLock)
            {
                // Cross-node single-flight: acquire the distributed lock with the same wait budget the local lock
                // used (soft timeout when a fail-safe stale reserve can absorb the elapse, LockTimeout otherwise),
                // so the degradation semantics on elapse mirror the local lock-timeout path exactly.
                var distributedLockTimeout = _SelectLockTimeout(options, staleCandidate, now);

                try
                {
                    distributedLease = await factoryLockProvider!
                        .TryAcquireAsync(key, distributedLockTimeout, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (!IsCallerCancellation(exception, cancellationToken))
                {
                    // A throwing acquire means the lock backend is down (vs null = held elsewhere). Stale beats
                    // failure: with fail-safe and a usable stale reserve, serve it without running the factory and
                    // restamp the throttle so per-call retries stop hammering the down backend. With no usable
                    // reserve the provider's failure propagates, mirroring the factory-throw fail-safe path.
                    now = _GetUtcNow();

                    if (!options.IsFailSafeEnabled || !_IsStaleCandidate(staleCandidate, now))
                    {
                        throw;
                    }

                    await _TryRestampStaleAsync(store, key, staleCandidate, options, now, CancellationToken.None)
                        .ConfigureAwait(false);

                    _logger.LogCacheFactoryLockAcquireFailed(exception, key, exception.GetType().Name);
                    return _ToCacheValue(staleCandidate, isStale: true);
                }

                if (distributedLease is null)
                {
                    _logger.LogCacheFactoryTimedOut(
                        key,
                        staleCandidate.Found ? "distributed-lock-soft" : "distributed-lock-timeout",
                        distributedLockTimeout
                    );

                    return _ToCacheValue(staleCandidate, isStale: true);
                }

                // The previous lock owner on another node may have just written a fresh value to the shared store;
                // re-check before running the factory so the loser of the cross-node race serves the winner's value.
                // Force-refresh skips this re-check as well: the caller asked to bypass the cached read on both
                // tiers unconditionally, so a peer's fresh write must not short-circuit the forced factory run.
                if (!options.SkipCacheRead)
                {
                    entry = await _TryGetEntryAsync<T>(store, key, readOptions, cancellationToken)
                        .ConfigureAwait(false);
                    now = _GetUtcNow();

                    if (entry.IsFresh(now))
                    {
                        await _TryRearmSlidingEntryAsync(store, key, entry, now).ConfigureAwait(false);
                        return _ToCacheValue(entry, isStale: false);
                    }

                    if (_ShouldServeStaleImmediately(options, entry, now))
                    {
                        return _ToCacheValue(entry, isStale: true);
                    }

                    if (_IsStaleCandidate(entry, now))
                    {
                        staleCandidate = entry;
                    }
                }
            }

            // One context per factory execution, built from the current physically-present entry so the factory
            // can see the last-known-good value and its validators (conditional refresh) and mutate the options
            // and tags it will be written with (adaptive caching).
            var context = _CreateFactoryContext(key, entry, options, now);

            var timeoutSelection = _SelectFactoryTimeout(options, staleCandidate, now, key);
            FactoryRunResult<CacheFactoryResult<T>> factoryResult;

            try
            {
                // Pass context and factory as separate parameters so _RunFactoryWithTimeoutAsync can invoke
                // factory(context, token) directly — no per-call closure allocation for the bound delegate.
                factoryResult = await _RunFactoryWithTimeoutAsync(
                        factory,
                        context,
                        key,
                        timeoutSelection.Timeout,
                        cancelOnTimeout: timeoutSelection.Kind == FactoryTimeoutKind.Hard,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (!IsCallerCancellation(exception, cancellationToken))
            {
                now = _GetUtcNow();

                if (!options.IsFailSafeEnabled || !_IsStaleCandidate(staleCandidate, now))
                {
                    throw;
                }

                await _TryRestampStaleAsync(store, key, staleCandidate, options, now, CancellationToken.None)
                    .ConfigureAwait(false);

                _logger.LogFailSafeActivated(key, exception.GetType().Name);
                return _ToCacheValue(staleCandidate, isStale: true);
            }

            if (factoryResult.IsTimedOut)
            {
                _logger.LogCacheFactoryTimedOut(
                    key,
                    _TimeoutKindLabel(timeoutSelection.Kind),
                    timeoutSelection.Timeout
                );

                if (factoryResult.IsSoftTimeout)
                {
                    // The distributed lease (when present) transfers into the background completion together with
                    // the local lock releaser, so the cross-node guard stays held until the detached factory lands.
                    _StartBackgroundCompletion(
                        store,
                        key,
                        context,
                        factoryResult.RunningTask,
                        factoryResult.InternalCancellationTokenSource,
                        staleCandidate,
                        options,
                        releaser,
                        distributedLease
                    );

                    ownsReleaser = false;
                    return _ToCacheValue(staleCandidate, isStale: true);
                }

                _ObserveFaultedTask(factoryResult.RunningTask, key);
                // A non-cooperative factory may ignore the hard-timeout cancellation and later complete
                // successfully; its result is discarded (the caller already moved on). Log that at Debug so the
                // wasted work is visible. Disjoint with the fault observer (different continuation predicates).
                _ObserveDiscardedSuccess(factoryResult.RunningTask, key);

                now = _GetUtcNow();

                if (_IsStaleCandidate(staleCandidate, now))
                {
                    await _TryRestampStaleAsync(store, key, staleCandidate, options, now, CancellationToken.None)
                        .ConfigureAwait(false);

                    _logger.LogFailSafeActivated(key, nameof(CacheFactoryTimeoutException));
                    return _ToCacheValue(staleCandidate, isStale: true);
                }

                throw new CacheFactoryTimeoutException(key, timeoutSelection.Timeout);
            }

            return await _WriteFactoryResultAsync(
                    store,
                    key,
                    context,
                    factoryResult.Value,
                    sourceEntry: entry,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        finally
        {
            if (ownsReleaser)
            {
                // Release in reverse acquisition order: free the distributed lease (so other nodes can proceed)
                // before the local per-key lock. Covers every foreground completion path — fresh write, fail-safe
                // return, hard timeout, and exceptions; the soft-timeout path transfers ownership instead.
                if (distributedLease is not null)
                {
                    await _ReleaseFactoryLockAsync(distributedLease, key).ConfigureAwait(false);
                }

                releaser.Dispose();
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _keyedLock.Dispose();
    }

    // Builds the per-execution factory context from the current physically-present entry: the last-known-good
    // value (including a cached null) plus its validators and tags. When no physical entry exists the context is
    // cold (HasStaleValue false, NoValue, null validators).
    private static CacheFactoryContext<T> _CreateFactoryContext<T>(
        string key,
        CacheStoreEntry<T> entry,
        CacheEntryOptions options,
        DateTime now
    )
    {
        if (!entry.IsPhysicallyPresent(now))
        {
            return new CacheFactoryContext<T>(CacheValue<T>.NoValue)
            {
                Key = key,
                Options = options,
                Tags = options.Tags,
            };
        }

        var staleValue = entry.IsNull
            ? new CacheValue<T>(default, hasValue: true)
            : new CacheValue<T>(entry.Value, hasValue: true);

        return new CacheFactoryContext<T>(staleValue)
        {
            Key = key,
            ETag = entry.ETag,
            LastModifiedAt = entry.LastModifiedAt,
            Options = options,
            // Call-provided tags win; otherwise carry the existing entry's tags forward.
            Tags = options.Tags ?? entry.Tags,
        };
    }

    // Persists a factory result as a fresh entry and returns the caller-facing value. Shared by the foreground
    // path, soft-timeout background completion, and eager refresh so conditional refresh (NotModified) and
    // adaptive options behave identically everywhere. A store-write failure on this path must propagate rather
    // than activate fail-safe (which would discard the fresh result).
    private async ValueTask<CacheValue<T>> _WriteFactoryResultAsync<T>(
        IFactoryCacheStore store,
        string key,
        CacheFactoryContext<T> context,
        CacheFactoryResult<T> result,
        CacheStoreEntry<T> sourceEntry,
        CancellationToken cancellationToken
    )
    {
        // Adaptive caching: the factory may have replaced the context's options. Re-validate before writing so an
        // invalid adaptive mutation throws (after the factory ran) instead of persisting a malformed entry.
        var options = context.Options;
        _ValidateOptions(options);
        // Factory-mutated context tags bypass options validation; hold them to the same envelope limits.
        CacheEntryStamps.ValidateTags(context.Tags, paramName: "context.Tags");

        T? value;
        string? eTag;
        DateTime? lastModifiedAt;

        if (result.IsNotModified)
        {
            if (!context.HasStaleValue)
            {
                // Unreachable through CacheFactoryContext<T>.NotModified() (which throws first); guards a
                // hand-constructed result so a cold miss can never silently cache a default value.
                throw new InvalidOperationException(
                    $"Cannot apply a NotModified result for cache key '{key}': no cached value exists to extend."
                );
            }

            // Conditional refresh: re-stamp the existing last-known-good value as fresh, preserving its
            // validators. A NotModified result's own validators are ignored.
            value = context.StaleValue.Value;
            eTag = context.ETag;
            lastModifiedAt = context.LastModifiedAt;
        }
        else
        {
            value = result.Value;
            eTag = result.ETag;
            lastModifiedAt = result.LastModifiedAt;
        }

        var now = _GetUtcNow();
        var stamps = CacheEntryStamps.Compute(options, now);

        // A NotModified extension re-stamps the existing value rather than producing a new one, so it must keep the
        // original birth time: carry the source entry's CreatedAt forward (falling back to now only if the source
        // had none, e.g. a legacy/unframed reserve). A genuine new value write stamps CreatedAt = now.
        var createdAt = result.IsNotModified ? sourceEntry.CreatedAt ?? stamps.CreatedAt : stamps.CreatedAt;

        var entry = new CacheStoreEntryWrite<T>
        {
            Value = value,
            IsNull = value is null,
            LogicalExpiresAt = stamps.LogicalExpiresAt,
            PhysicalExpiresAt = stamps.PhysicalExpiresAt,
            SlidingExpiration = options.SlidingExpiration,
            EagerRefreshAt = stamps.EagerRefreshAt,
            ETag = eTag,
            LastModifiedAt = lastModifiedAt,
            CreatedAt = createdAt,
            Tags = context.Tags,
            ExpectedConcurrencyStamp = sourceEntry.ConcurrencyStamp,
            // A NotModified extension re-stamps the existing value: peers' cached bytes stay valid, so
            // multi-tier stores must not broadcast an invalidation for it.
            IsRestamp = result.IsNotModified,
            // Per-call tier-write control, taken from the (possibly adaptively-replaced) context options the same
            // way Tags are, so a conditional factory mutating its options also governs which tiers are written.
            SkipMemoryCacheWrite = options.SkipMemoryCacheWrite,
            SkipDistributedCacheWrite = options.SkipDistributedCacheWrite,
        };

        var persisted = await store.SetEntryAsync(key, in entry, cancellationToken).ConfigureAwait(false);

        if (!persisted)
        {
            // CAS lost: a concurrent write changed the entry under the lock between our read and this set. Best-effort
            // by design — the freshly computed value still goes to the caller and the next read recomputes; we only
            // surface the loss for observability and never retry (a retry would re-run the factory under the lock).
            _logger.LogCacheFactoryWriteLostToConcurrentWrite(key);
        }

        return new CacheValue<T>(value, hasValue: true);
    }

    private async ValueTask<CacheStoreEntry<T>> _TryGetEntryAsync<T>(
        IFactoryCacheStore store,
        string key,
        FactoryCacheReadOptions readOptions,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await store.TryGetEntryAsync<T>(key, cancellationToken, readOptions).ConfigureAwait(false);
        }
        catch (Exception exception) when (!IsCallerCancellation(exception, cancellationToken))
        {
            _logger.LogCacheStoreReadFailed(exception, key);
            return CacheStoreEntry<T>.NotFound;
        }
    }

    private static CacheValue<T> _ToCacheValue<T>(CacheStoreEntry<T> entry, bool isStale)
    {
        if (!entry.Found)
        {
            return CacheValue<T>.NoValue;
        }

        return entry.IsNull
            ? new CacheValue<T>(default, hasValue: true, isStale)
            : new CacheValue<T>(entry.Value, hasValue: true, isStale);
    }

    // A fail-safe stale candidate must carry a non-null physical expiration. A genuine fail-safe reserve
    // always has one (the coordinator writes it); requiring it here closes the throttle hole where a
    // null-physical entry would be served as stale without a throttle write, hammering the factory.
    private static bool _IsStaleCandidate<T>(CacheStoreEntry<T> entry, DateTime now)
    {
        return entry.IsPhysicallyPresent(now) && entry.PhysicalExpiresAt.HasValue;
    }

    // Serve-stale-immediately: with fail-safe enabled and a usable stale reserve, an entry flagged
    // ServeStaleImmediately is returned without contending for the factory lock. Deduped across the three
    // GetOrAddAsync re-check points (pre-lock, under-lock, after distributed-lock acquire).
    private static bool _ShouldServeStaleImmediately<T>(
        CacheEntryOptions options,
        CacheStoreEntry<T> entry,
        DateTime now
    )
    {
        return entry.ServeStaleImmediately && options.IsFailSafeEnabled && _IsStaleCandidate(entry, now);
    }

    // Soft timeout governs the waiter wait only when a stale reserve can be served on elapse; otherwise the
    // waiter is bounded by LockTimeout (default Timeout.InfiniteTimeSpan), and on elapse with no stale reserve
    // the waiter degrades to a miss. This mirrors FusionCache's GetAppropriateMemoryLockTimeout: a stale + fail-safe
    // + finite-soft caller waits FactorySoftTimeout, every other caller waits the base LockTimeout.
    private static TimeSpan _SelectLockTimeout<T>(
        CacheEntryOptions options,
        CacheStoreEntry<T> staleCandidate,
        DateTime now
    )
    {
        return
            options.IsFailSafeEnabled
            && _IsStaleCandidate(staleCandidate, now)
            && options.FactorySoftTimeout != Timeout.InfiniteTimeSpan
            ? options.FactorySoftTimeout
            : options.LockTimeout;
    }

    private FactoryTimeoutSelection _SelectFactoryTimeout<T>(
        CacheEntryOptions options,
        CacheStoreEntry<T> staleCandidate,
        DateTime now,
        string key
    )
    {
        if (options.FactorySoftTimeout != Timeout.InfiniteTimeSpan && !options.IsFailSafeEnabled)
        {
            // Cap the per-key dedup set so a high-cardinality key space under this misconfiguration cannot grow
            // memory without bound; the Count check before TryAdd may overshoot slightly under concurrency, which
            // is harmless for a warn-once notice.
            if (_softTimeoutInertWarnings.Count < _MaxInertWarningKeys && _softTimeoutInertWarnings.TryAdd(key, 0))
            {
                _logger.LogCacheSoftTimeoutInert(key, options.FactorySoftTimeout);
            }
            // Once the per-key warnings are capped, further inert keys are silently suppressed. Re-emit the cap
            // notice every _InertCapReLogInterval suppressed occurrences (not once-only) so the misconfiguration
            // stays observable over a long-lived service. Allocation-free: a single Interlocked.Increment with a
            // modulo check on the hot timeout-selection path.
            else if (_softTimeoutInertWarnings.Count >= _MaxInertWarningKeys)
            {
                var suppressed = Interlocked.Increment(ref _softTimeoutInertCapSuppressed);

                if (suppressed % _InertCapReLogInterval == 1)
                {
                    _logger.LogCacheSoftTimeoutInertCapReached(_MaxInertWarningKeys, suppressed);
                }
            }
        }

        var hasFallback = options.IsFailSafeEnabled && _IsStaleCandidate(staleCandidate, now);

        if (hasFallback && options.FactorySoftTimeout != Timeout.InfiniteTimeSpan)
        {
            return new FactoryTimeoutSelection(FactoryTimeoutKind.Soft, options.FactorySoftTimeout);
        }

        if (options.FactoryHardTimeout != Timeout.InfiniteTimeSpan)
        {
            return new FactoryTimeoutSelection(FactoryTimeoutKind.Hard, options.FactoryHardTimeout);
        }

        return new FactoryTimeoutSelection(FactoryTimeoutKind.None, Timeout.InfiniteTimeSpan);
    }

    // `factory` and `context` are passed as separate parameters (rather than a pre-bound closure) so the caller
    // does not allocate a display class on each cache miss — factory(context, token) is invoked inline here.
    private async ValueTask<FactoryRunResult<CacheFactoryResult<T>>> _RunFactoryWithTimeoutAsync<T>(
        Func<CacheFactoryContext<T>, CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
        CacheFactoryContext<T> context,
        string key,
        TimeSpan timeout,
        bool cancelOnTimeout,
        CancellationToken cancellationToken
    )
    {
        // No factory timeout configured: skip the delay timer/race machinery. A non-cancellable caller cannot
        // interrupt the factory, so await it directly (the common default-options hot path); allocate nothing.
        if (timeout == Timeout.InfiniteTimeSpan && !cancellationToken.CanBeCanceled)
        {
            var directValue = await factory(context, cancellationToken).ConfigureAwait(false);
            return FactoryRunResult<CacheFactoryResult<T>>.Completed(directValue);
        }

        CancellationTokenSource? internalCts = new();
        CancellationTokenRegistration cancellationRegistration = default;

        try
        {
            var factoryTask = factory(context, internalCts.Token).AsTask();
            Task? callerCancellationTask = null;

            if (cancellationToken.CanBeCanceled)
            {
                var cancellationTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                cancellationRegistration = cancellationToken.Register(
                    static state => ((TaskCompletionSource)state!).TrySetResult(),
                    cancellationTcs
                );
                callerCancellationTask = cancellationTcs.Task;
            }

            if (timeout == Timeout.InfiniteTimeSpan)
            {
                // Cancellable caller, no factory timeout: race the factory against caller cancellation only, with
                // no delay timer. R6 (caller cancellation wins) still holds, even against a non-cooperative factory.
                var untimedWinner = await Task.WhenAny(factoryTask, callerCancellationTask!).ConfigureAwait(false);

                if (untimedWinner == callerCancellationTask)
                {
                    await internalCts.CancelAsync().ConfigureAwait(false);
                    _ObserveFaultedTask(factoryTask, key);
                    // Defer disposal until the abandoned factory finishes so a non-cooperative factory never
                    // touches a disposed token source; null out so the finally does not dispose it under the factory.
                    CacheDetachedTask.DisposeAfter(internalCts, factoryTask);
                    internalCts = null;
                    throw new OperationCanceledException(cancellationToken);
                }

                return FactoryRunResult<CacheFactoryResult<T>>.Completed(await factoryTask.ConfigureAwait(false));
            }

            using var delayCts = new CancellationTokenSource();
            var delayTask = Task.Delay(timeout, _timeProvider, delayCts.Token);
            FactoryTimeoutTimerRegistered?.Invoke();

            var winner = callerCancellationTask is null
                ? await Task.WhenAny(factoryTask, delayTask).ConfigureAwait(false)
                : await Task.WhenAny(factoryTask, delayTask, callerCancellationTask).ConfigureAwait(false);

            if (winner == factoryTask)
            {
                await delayCts.CancelAsync().ConfigureAwait(false);
                var value = await factoryTask.ConfigureAwait(false);
                return FactoryRunResult<CacheFactoryResult<T>>.Completed(value);
            }

            if (winner == callerCancellationTask)
            {
                await internalCts.CancelAsync().ConfigureAwait(false);
                // The caller abandoned the wait but a non-cooperative factory may keep running and later fault.
                // Observe its task so the fault is logged rather than lost, mirroring the hard-timeout and
                // background-ceiling abandonment paths.
                _ObserveFaultedTask(factoryTask, key);
                // Defer disposal until the abandoned factory finishes so it never touches a disposed token source;
                // null out so the finally does not dispose it while the factory still holds the token.
                CacheDetachedTask.DisposeAfter(internalCts, factoryTask);
                internalCts = null;
                throw new OperationCanceledException(cancellationToken);
            }

            if (cancelOnTimeout)
            {
                await internalCts.CancelAsync().ConfigureAwait(false);
                // The factory is abandoned but may keep running; the caller observes its fault at the hard-timeout
                // branch. Defer CTS disposal until the factory finishes so it never touches a disposed token source.
                CacheDetachedTask.DisposeAfter(internalCts, factoryTask);
                internalCts = null;
                return FactoryRunResult<CacheFactoryResult<T>>.TimedOut(
                    factoryTask,
                    internalCancellationTokenSource: null
                );
            }

            await delayCts.CancelAsync().ConfigureAwait(false);
            var transferredCts = internalCts;
            internalCts = null;
            return FactoryRunResult<CacheFactoryResult<T>>.TimedOut(factoryTask, transferredCts);
        }
        finally
        {
            await cancellationRegistration.DisposeAsync().ConfigureAwait(false);
            internalCts?.Dispose();
        }
    }

    // Caller cancellation (the caller's own token) must always propagate and never activate fail-safe (KTD-7).
    // Use token identity, not just IsCancellationRequested, so an OperationCanceledException raised by an
    // unrelated linked/internal token (e.g. a downstream timeout) still activates fail-safe.
    /// <summary>
    /// Returns whether <paramref name="exception"/> represents cancellation of the caller's own token, which must
    /// always propagate rather than activate fail-safe (KTD-7). An <see cref="OperationCanceledException"/> raised
    /// by an unrelated linked/internal token (for example a downstream timeout) is NOT caller cancellation and
    /// should activate fail-safe / degrade to a miss. Providers composing this engine (e.g. a hybrid store)
    /// should use this predicate for their best-effort catch filters so cancellation semantics stay consistent.
    /// </summary>
    /// <param name="exception">The exception thrown by the factory or store operation.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    [Pure]
    public static bool IsCallerCancellation(Exception exception, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return true;
        }

        // Identity-match only when the caller supplied a cancellable token. CancellationToken.None compares equal
        // to a token-less OperationCanceledException's token (default == default), which would otherwise wrongly
        // suppress fail-safe for a downstream OCE when the caller passed no token (the default-token call shape).
        return cancellationToken.CanBeCanceled
            && exception is OperationCanceledException operationCanceled
            && operationCanceled.CancellationToken == cancellationToken;
    }

    private DateTime _GetUtcNow()
    {
        return _timeProvider.GetUtcNow().UtcDateTime;
    }

    private static DateTime _Min(DateTime left, DateTime right)
    {
        return left <= right ? left : right;
    }

    // Keep the structured TimeoutKind log value consistent (kebab/lower) with the other LogCacheFactoryTimedOut
    // call sites (lock-soft, lock-timeout, background-ceiling) so log-based monitoring can match on one shape.
    private static string _TimeoutKindLabel(FactoryTimeoutKind kind)
    {
        return kind switch
        {
            FactoryTimeoutKind.Soft => "soft",
            FactoryTimeoutKind.Hard => "hard",
            _ => "none",
        };
    }

    // Attach a fault-only observer to a detached task (an abandoned factory or a background completion) so its
    // exception is logged rather than lost. Task<T?> upcasts to Task, so both call shapes share this one observer.
    private void _ObserveFaultedTask(Task task, string key)
    {
        _ = task.ContinueWith(
            faulted =>
                // Log the inner exception's type, not the AggregateException wrapper a faulted Task carries, so the
                // {ExceptionType} field matches the bare-exception call sites in the Observe* completion paths.
                _logger.LogCacheBackgroundCompletionFailed(
                    faulted.Exception!,
                    key,
                    (faulted.Exception!.InnerException ?? faulted.Exception!).GetType().Name
                ),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default
        );
    }

    // Companion to _ObserveFaultedTask: when a hard-timed-out, non-cooperative factory ignores cancellation and
    // later completes successfully, the result is discarded (the caller already returned). The OnlyOnRanToCompletion
    // predicate is disjoint from the fault observer's OnlyOnFaulted, so both can be attached without interfering.
    private void _ObserveDiscardedSuccess(Task task, string key)
    {
        _ = task.ContinueWith(
            static (_, state) =>
            {
                var (logger, cacheKey) = ((ILogger, string))state!;
                logger.LogCacheFactoryDiscardedSuccess(cacheKey);
            },
            (_logger, key),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default
        );
    }

    // The validation rules live in CacheEntryStamps so the coordinator and the providers' direct
    // options-based upserts always agree; keep this thin wrapper for call-site brevity.
    private static void _ValidateOptions(CacheEntryOptions options)
    {
        CacheEntryStamps.ValidateOptions(options);
    }

    private enum FactoryTimeoutKind
    {
        None,
        Soft,
        Hard,
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct FactoryTimeoutSelection(FactoryTimeoutKind Kind, TimeSpan Timeout);

    [StructLayout(LayoutKind.Auto)]
    private readonly struct FactoryRunResult<T>
    {
        private FactoryRunResult(
            T? value,
            Task<T?>? runningTask,
            CancellationTokenSource? internalCancellationTokenSource
        )
        {
            Value = value;
            RunningTask = runningTask;
            InternalCancellationTokenSource = internalCancellationTokenSource;
        }

        public T? Value { get; }

        public Task<T?>? RunningTask { get; }

        public CancellationTokenSource? InternalCancellationTokenSource { get; }

        [MemberNotNullWhen(true, nameof(RunningTask))]
        public bool IsTimedOut => RunningTask is not null;

        // A soft timeout transfers the internal CTS to the caller so the factory can complete in the background;
        // a hard timeout cancels and discards it (null). The CTS presence is therefore the soft/hard discriminant.
        [MemberNotNullWhen(true, nameof(RunningTask), nameof(InternalCancellationTokenSource))]
        public bool IsSoftTimeout => InternalCancellationTokenSource is not null;

        public static FactoryRunResult<T> Completed(T? value)
        {
            return new(value, runningTask: null, internalCancellationTokenSource: null);
        }

        public static FactoryRunResult<T> TimedOut(
            Task<T?> runningTask,
            CancellationTokenSource? internalCancellationTokenSource
        )
        {
            return new(value: default, runningTask, internalCancellationTokenSource);
        }
    }

    // Wraps a simple Func<CancellationToken, ValueTask<T?>> so the simple GetOrAddAsync overload can form a
    // delegate from an instance method rather than a capturing lambda. The struct is boxed once (for the delegate
    // target), but the compiler-generated display class and its factory capture are eliminated — one fewer heap
    // object on every simple-factory cache miss.
    private readonly struct SimpleFactoryAdapter<T>(Func<CancellationToken, ValueTask<T?>> factory)
    {
        public async ValueTask<CacheFactoryResult<T>> InvokeAsync(
            CacheFactoryContext<T> context,
            CancellationToken cancellationToken
        )
        {
            return context.Modified(await factory(cancellationToken).ConfigureAwait(false));
        }
    }
}

internal static partial class FactoryCacheCoordinatorLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "CacheFailSafeActivated",
        Level = LogLevel.Warning,
        Message = "Cache fail-safe activated for key {Key}; serving stale value after factory exception {ExceptionType}."
    )]
    public static partial void LogFailSafeActivated(this ILogger logger, string key, string exceptionType);

    [LoggerMessage(
        EventId = 2,
        EventName = "CacheFailSafeRestampFailed",
        Level = LogLevel.Warning,
        Message = "Cache fail-safe restamp failed for key {Key}; stale value will still be returned, but the "
            + "throttle window was not persisted so the factory may be retried sooner than expected."
    )]
    public static partial void LogFailSafeRestampFailed(this ILogger logger, Exception exception, string key);

    [LoggerMessage(
        EventId = 3,
        EventName = "CacheStoreReadFailed",
        Level = LogLevel.Debug,
        Message = "Cache store read failed for key {Key}; treating it as a cache miss."
    )]
    public static partial void LogCacheStoreReadFailed(this ILogger logger, Exception exception, string key);

    [LoggerMessage(
        EventId = 4,
        EventName = "CacheFactoryTimedOut",
        Level = LogLevel.Warning,
        Message = "Cache factory timeout fired for key {Key}; kind={TimeoutKind}, limit={Timeout}."
    )]
    public static partial void LogCacheFactoryTimedOut(
        this ILogger logger,
        string key,
        string timeoutKind,
        TimeSpan timeout
    );

    [LoggerMessage(
        EventId = 5,
        EventName = "CacheBackgroundCompletionSucceeded",
        Level = LogLevel.Debug,
        Message = "Cache background completion succeeded for key {Key}."
    )]
    public static partial void LogCacheBackgroundCompletionSucceeded(this ILogger logger, string key);

    [LoggerMessage(
        EventId = 6,
        EventName = "CacheBackgroundCompletionFailed",
        Level = LogLevel.Warning,
        Message = "Cache background completion failed for key {Key}; exception={ExceptionType}."
    )]
    public static partial void LogCacheBackgroundCompletionFailed(
        this ILogger logger,
        Exception exception,
        string key,
        string exceptionType
    );

    [LoggerMessage(
        EventId = 7,
        EventName = "CacheSoftTimeoutInert",
        Level = LogLevel.Warning,
        Message = "Cache factory soft timeout {Timeout} is configured for key {Key}, but fail-safe is disabled; the soft timeout is inert."
    )]
    public static partial void LogCacheSoftTimeoutInert(this ILogger logger, string key, TimeSpan timeout);

    [LoggerMessage(
        EventId = 14,
        EventName = "CacheSoftTimeoutInertCapReached",
        Level = LogLevel.Warning,
        Message = "Cache soft-timeout inert-warning suppression cap reached ({Cap} keys); {SuppressedCount} further "
            + "inert keys have been suppressed. This notice re-emits periodically while the misconfiguration persists."
    )]
    public static partial void LogCacheSoftTimeoutInertCapReached(this ILogger logger, int cap, long suppressedCount);

    [LoggerMessage(
        EventId = 8,
        EventName = "CacheSlidingExpirationRearmFailed",
        Level = LogLevel.Debug,
        Message = "Cache sliding-expiration re-arm failed for key {Key}; the cached value will still be returned."
    )]
    // Named "Coordinator…" (not the bare "LogSlidingExpirationRearmFailed") to avoid extension-method ambiguity
    // with provider log classes (e.g. RedisCacheLog) now that the provider assemblies see Core internals via
    // InternalsVisibleTo.
    public static partial void LogCoordinatorSlidingExpirationRearmFailed(
        this ILogger logger,
        Exception exception,
        string key
    );

    [LoggerMessage(
        EventId = 9,
        EventName = "CacheEagerRefreshSucceeded",
        Level = LogLevel.Debug,
        Message = "Cache eager refresh succeeded for key {Key}."
    )]
    public static partial void LogEagerRefreshSucceeded(this ILogger logger, string key);

    [LoggerMessage(
        EventId = 10,
        EventName = "CacheEagerRefreshFailed",
        Level = LogLevel.Warning,
        Message = "Cache eager refresh failed for key {Key}; exception={ExceptionType}. The entry stays fresh until natural expiry."
    )]
    public static partial void LogEagerRefreshFailed(
        this ILogger logger,
        Exception exception,
        string key,
        string exceptionType
    );

    [LoggerMessage(
        EventId = 11,
        EventName = "CacheEagerRefreshSkipped",
        Level = LogLevel.Debug,
        Message = "Cache eager refresh skipped for key {Key}: the gate write failed; the entry stays fresh and re-triggerable."
    )]
    public static partial void LogEagerRefreshSkipped(this ILogger logger, Exception exception, string key);

    [LoggerMessage(
        EventId = 12,
        EventName = "CacheFactoryLockReleaseFailed",
        Level = LogLevel.Warning,
        Message = "Cache distributed factory-lock release failed for key {Key}; the lease TTL is the backstop and "
            + "other nodes may be delayed until it expires."
    )]
    public static partial void LogCacheFactoryLockReleaseFailed(this ILogger logger, Exception exception, string key);

    [LoggerMessage(
        EventId = 13,
        EventName = "CacheFactoryLockAcquireFailed",
        Level = LogLevel.Warning,
        Message = "Cache distributed factory-lock acquire failed for key {Key}; serving stale value after lock "
            + "provider exception {ExceptionType}."
    )]
    public static partial void LogCacheFactoryLockAcquireFailed(
        this ILogger logger,
        Exception exception,
        string key,
        string exceptionType
    );

    [LoggerMessage(
        EventId = 15,
        EventName = "CacheFactoryWriteLostToConcurrentWrite",
        Level = LogLevel.Debug,
        Message = "Cache factory result for key {Key} was not persisted: a concurrent write changed the entry under "
            + "the lock; the value was returned to the caller and the next read will recompute."
    )]
    public static partial void LogCacheFactoryWriteLostToConcurrentWrite(this ILogger logger, string key);

    [LoggerMessage(
        EventId = 16,
        EventName = "CacheFactoryDiscardedSuccess",
        Level = LogLevel.Debug,
        Message = "Cache factory for key {Key} completed after a hard timeout but its result was discarded; the "
            + "factory ignored cancellation and the wasted work was thrown away."
    )]
    public static partial void LogCacheFactoryDiscardedSuccess(this ILogger logger, string key);

    [LoggerMessage(
        EventId = 17,
        EventName = "CacheEagerRefreshAbandonedGateEntryLost",
        Level = LogLevel.Debug,
        Message = "Cache eager refresh abandoned for key {Key}: the post-gate re-read returned no live entry (the key "
            + "was concurrently removed or the re-read failed), so the eager write is dropped rather than resurrecting "
            + "the key; natural expiry and the next read take over."
    )]
    public static partial void LogEagerRefreshAbandonedGateEntryLost(this ILogger logger, string key);
}
