// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Headless.Caching;

/// <summary>Kind of pending operation tracked by <see cref="HybridCacheRecoveryQueue"/>.</summary>
internal enum HybridCacheRecoveryKind
{
    SetEntry,
    Remove,
    Expire,
    PublishInvalidation,

    /// <summary>
    /// A Family-2 tag/clear/remove generation marker bump (logical RemoveByTag/Clear/Flush). Stored under a
    /// synthetic key; replay re-asserts the marker at its original timestamp (raise-only durable write) and
    /// re-broadcasts. Exempt from <see cref="HybridCacheRecoveryQueue.OnIncomingInvalidation"/> conflict drops —
    /// raise-only markers are idempotent and never resurrect stale data.
    /// </summary>
    MarkerBump,
}

/// <summary>Outcome of replaying a queued recovery item.</summary>
internal enum HybridCacheRecoveryReplayOutcome
{
    /// <summary>The pending operation was replayed against the recovered dependency.</summary>
    Replayed,

    /// <summary>The pending operation no longer applies (e.g. the L1 entry changed) and was dropped.</summary>
    Obsolete,
}

/// <summary>
/// Bounded queue of pending L2/backplane operations used by <see cref="HybridCache"/> auto-recovery (design
/// reference: FusionCache's AutoRecoveryService, adapted). One pending operation per cache key with kind-aware
/// coalescing: a newer value op (set/remove) replaces any queued item, but an invalidation publish never
/// displaces a queued value op — the value op subsumes it because its successful replay publishes the key
/// invalidation itself. Any successful L2 write for a key clears its pending item, so a surviving item always
/// represents the latest local intent for that key.
/// </summary>
/// <remarks>
/// Scope covers single-key operations (factory/entry sets, scalar upserts, removes, and their invalidation
/// publishes) plus Family-2 tag/clear/remove marker bumps (<see cref="HybridCacheRecoveryKind.MarkerBump"/>, stored
/// under synthetic keys and replayed at their original timestamp via a raise-only durable write). Bulk, atomic
/// (increment/set-if), and set operations are not captured: their failure semantics are value-dependent and
/// replaying them later could double-apply effects.
/// </remarks>
internal sealed class HybridCacheRecoveryQueue : IDisposable
{
    private readonly HybridCacheOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, RecoveryItem> _items = new(StringComparer.Ordinal);
    private readonly Lock _admissionLock = new();
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly ITimer _timer;

    private long _barrierUntilTicks;
    private int _processing;
    private int _isDisposed;
    private RecoveryItem? _replaying;

    // Signals completion of the currently-gated recovery pass so DrainAsync can await it on disposal. Published by
    // _OnTimerTick only AFTER it wins the _processing gate (and owned by that pass alone), so a gate-blocked no-op
    // tick never overwrites a running pass's signal with an immediately-completed one — the hole that previously let
    // DrainAsync return while a real pass was still replaying to L2. RunContinuationsAsynchronously keeps the awaiting
    // DrainAsync off the thread that completes the source.
    private volatile TaskCompletionSource? _activeProcessTcs;

    // Tracks the item with the earliest ExpiresAt so the queue-full eviction path avoids an O(N) scan.
    // Maintained incrementally on every add inside _admissionLock; invalidated (set to null) when that tracked item
    // is removed so the next queue-full add triggers a one-time O(N) re-scan to find the new minimum.
    private RecoveryItem? _minExpiryItem;

    public HybridCacheRecoveryQueue(HybridCacheOptions options, TimeProvider timeProvider, ILogger logger)
    {
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;

        // TimeProvider-driven timer so FakeTimeProvider tests can drive the loop deterministically.
        _timer = timeProvider.CreateTimer(
            static state => ((HybridCacheRecoveryQueue)state!)._OnTimerTick(),
            this,
            options.AutoRecoveryDelay,
            options.AutoRecoveryDelay
        );
    }

    /// <summary>Number of pending recovery items (test/diagnostic hook).</summary>
    internal int Count => _items.Count;

    /// <summary>
    /// Whether a pending item exists for the given key. <see cref="HybridCache.HandleInvalidationAsync"/> uses
    /// it after the <see cref="OnIncomingInvalidation"/> conflict pass: a surviving item is local intent at
    /// least as new as the incoming message, so the message must not wipe the local L1 entry.
    /// </summary>
    internal bool Contains(string key) => _items.ContainsKey(key);

    /// <summary>Kind of the pending item for the given key, when one exists (test/diagnostic hook).</summary>
    internal HybridCacheRecoveryKind? GetKind(string key) => _items.TryGetValue(key, out var item) ? item.Kind : null;

    /// <summary>
    /// Default retention window for items without a natural expiry (removes, publishes, sets without TTL).
    /// </summary>
    internal TimeSpan DefaultRetention => _options.AutoRecoveryDelay * _options.AutoRecoveryMaxRetries;

    /// <summary>
    /// Queues a pending operation for the key with kind-aware coalescing: a value op (set/remove) replaces any
    /// queued item (last intent wins), a publish refreshes a queued publish, but a publish never displaces a
    /// queued value op — the value op subsumes it because its successful replay publishes the key invalidation
    /// itself. On overflow the item with the earliest expiry (including the incoming one) is sacrificed to keep
    /// the items most likely to still matter when the dependency recovers.
    /// </summary>
    /// <param name="enqueuedAt">
    /// Overrides the item's intent timestamp (used by replay ordering and the incoming-invalidation conflict
    /// check). Residual publishes pass the original write time so a foreign write that raced between the
    /// original write and its replay still wins the conflict check.
    /// </param>
    public void Enqueue(
        string key,
        HybridCacheRecoveryKind kind,
        DateTimeOffset expiresAt,
        Func<CancellationToken, ValueTask<HybridCacheRecoveryReplayOutcome>> replay,
        DateTimeOffset? enqueuedAt = null
    )
    {
        var now = _timeProvider.GetUtcNow();

        if (expiresAt <= now)
        {
            return; // Nothing left to recover.
        }

        var item = new RecoveryItem(key, kind, enqueuedAt ?? now, expiresAt, replay);

        lock (_admissionLock)
        {
            if (_items.TryGetValue(key, out var existing))
            {
                // A queued value op must survive a publish for the same key: displacing it would lose the value
                // permanently while its replay already covers the invalidation. The one exception is the value
                // op currently being replayed — its L2 write has already landed, so a residual publish is the
                // correct remaining intent for the key.
                if (
                    kind == HybridCacheRecoveryKind.PublishInvalidation
                    && existing.Kind != HybridCacheRecoveryKind.PublishInvalidation
                    && !ReferenceEquals(existing, Volatile.Read(ref _replaying))
                )
                {
                    _logger.LogAutoRecoveryPublishSubsumed(key, existing.Kind);
                    return;
                }
            }
            else if (_items.Count >= _options.AutoRecoveryMaxItems)
            {
                var victim = _FindEarliestExpiry();

                if (victim is not null && victim.ExpiresAt < expiresAt)
                {
                    _items.TryRemove(victim.Key, out _);

                    // The minimum-expiry item is being evicted; invalidate the tracked pointer so the next
                    // queue-full add re-scans for the new minimum rather than comparing against a stale reference.
                    if (ReferenceEquals(_minExpiryItem, victim))
                    {
                        _minExpiryItem = null;
                    }

                    _logger.LogAutoRecoveryItemEvicted(victim.Key, victim.Kind);
                }
                else
                {
                    // The incoming item is itself the earliest-expiring one: reject it instead.
                    _logger.LogAutoRecoveryItemRejected(key, kind);
                    return;
                }
            }

            _items[key] = item;

            // Maintain the incremental minimum-expiry pointer: cheap compare on every add, O(N) re-scan only
            // after the tracked minimum is evicted (above) or removed via OnSuccessfulL2Operation/conflict.
            if (_minExpiryItem is null || item.ExpiresAt < _minExpiryItem.ExpiresAt)
            {
                _minExpiryItem = item;
            }
        }

        _logger.LogAutoRecoveryItemQueued(key, kind);
    }

    /// <summary>
    /// Clears the pending item for a key after a successful L2 operation for that key: the success supersedes
    /// whatever was queued, so replaying it would resurrect stale state.
    /// </summary>
    public void OnSuccessfulL2Operation(string key)
    {
        if (_items.TryRemove(key, out var item))
        {
            // Conservatively invalidate the tracked minimum: we don't hold _admissionLock here, so we can't
            // cheaply confirm whether this item was the minimum. Null forces a one-time re-scan on the next
            // queue-full add. Volatile ensures the write is visible to threads entering Enqueue.
            Volatile.Write(ref _minExpiryItem, value: null);
            _logger.LogAutoRecoveryItemSuperseded(key, item.Kind);
        }
    }

    /// <summary>
    /// Clears a queued <see cref="HybridCacheRecoveryKind.MarkerBump"/> after a successful live marker write, but
    /// ONLY when this write's generation (<paramref name="writtenAt"/>) is at least as new as the queued bump's
    /// intent timestamp. Marker bumps of different generations for the same tag coalesce onto one synthetic key, and
    /// the durable write is raise-only — so an OLDER live write must not drop a queued NEWER bump (that would leave
    /// the shared marker behind the newer invalidation). Unlike the scalar path, where any success for a key means
    /// the latest value landed, marker generations are not interchangeable.
    /// </summary>
    public void OnSuccessfulMarkerBump(string key, DateTimeOffset writtenAt)
    {
        if (!_items.TryGetValue(key, out var item) || item.EnqueuedAt > writtenAt)
        {
            return;
        }

        // Remove only if the slot still holds the same item we inspected (a newer bump may have replaced it).
        if (_items.TryRemove(KeyValuePair.Create(key, item)))
        {
            Volatile.Write(ref _minExpiryItem, value: null);
            _logger.LogAutoRecoveryItemSuperseded(key, item.Kind);
        }
    }

    /// <summary>
    /// Conflict check for incoming (foreign-instance) invalidations: a queued item older than the incoming
    /// message lost the race — another node wrote/invalidated the key after us, so replaying ours would
    /// resurrect stale data. A message without a timestamp is treated as newer (conservative drop). Tag
    /// invalidations are not matched because queued items are not indexed by tag.
    /// </summary>
    public void OnIncomingInvalidation(CacheInvalidationMessage message)
    {
        if (_items.IsEmpty)
        {
            return;
        }

        // A queued marker bump never resurrects stale data, so it is exempt from the conflict drop. This rests on two
        // invariants the caller (HybridCache._QueueMarkerRecovery / _BumpL2MarkerBestEffortAsync) must uphold:
        // (1) every MarkerBump replay uses a raise-only durable write (the ISeedableTagMarkerCache.Write* contract),
        // so replaying an old marker only re-asserts its generation and cannot lower a newer one; and (2) synthetic
        // marker keys (the "\0hybrid-marker:*" namespace) are never emitted as real cache keys in invalidation
        // messages, so they never match the key/prefix branches below.
        bool isConflicting(RecoveryItem item) =>
            item.Kind != HybridCacheRecoveryKind.MarkerBump
            && (message.Timestamp is null || item.EnqueuedAt < message.Timestamp.Value);

        if (message.FlushAll)
        {
            foreach (var pair in _items)
            {
                if (isConflicting(pair.Value))
                {
                    _TryRemoveConflicting(pair);
                }
            }

            return;
        }

        if (!string.IsNullOrEmpty(message.Prefix))
        {
            foreach (var pair in _items)
            {
                if (pair.Key.StartsWith(message.Prefix, StringComparison.Ordinal) && isConflicting(pair.Value))
                {
                    _TryRemoveConflicting(pair);
                }
            }

            return;
        }

        if (message.Keys is { Length: > 0 })
        {
            foreach (var key in message.Keys)
            {
                if (_items.TryGetValue(key, out var item) && isConflicting(item))
                {
                    _TryRemoveConflicting(new(key, item));
                }
            }

            return;
        }

        if (
            !string.IsNullOrEmpty(message.Key)
            && _items.TryGetValue(message.Key, out var single)
            && isConflicting(single)
        )
        {
            _TryRemoveConflicting(new(message.Key, single));
        }
    }

    /// <summary>
    /// Runs one processing pass: skipped while behind the failure barrier, drops expired items, then replays
    /// pending items in dictionary-enumeration order. A replay failure increments that item's retry count (and
    /// drops it at the cap) and arms the barrier so the NEXT pass is delayed, but the current pass continues to
    /// the remaining items — a single poison item never starves the healthy ones behind it. Never throws
    /// (cancellation included).
    /// </summary>
    /// <remarks>
    /// Per-tick snapshot+sort (ToArray+OrderBy) is intentionally avoided: ConcurrentDictionary's lock-free
    /// removal paths in <see cref="OnIncomingInvalidation"/> and <see cref="OnSuccessfulL2Operation"/> make it
    /// impossible to keep a sorted side-structure consistent without adding broad locking around those paths.
    /// Convergence is timestamp-driven (each item carries its original EnqueuedAt), so per-pass replay order
    /// does not affect correctness — the last writer always wins regardless of which item in a multi-item queue
    /// is replayed first.
    /// </remarks>
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _processing, 1, 0) != 0)
        {
            return; // A previous pass is still running; the timer fires again next cadence.
        }

        try
        {
            await _ProcessCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _processing, 0);
        }
    }

    /// <summary>
    /// The replay pass body, WITHOUT the <see cref="_processing"/> gate. The caller MUST already hold the gate:
    /// <see cref="ProcessAsync"/> acquires it before delegating here, and the timer path acquires it in
    /// <see cref="_OnTimerTick"/> before publishing the drain signal — so a gate-blocked no-op tick never publishes
    /// an already-completed drain signal that would let disposal's <see cref="DrainAsync"/> return while a real pass
    /// is still replaying. Never throws (cancellation included).
    /// </summary>
    private async Task _ProcessCoreAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        if (now.UtcTicks < Volatile.Read(ref _barrierUntilTicks) || _items.IsEmpty)
        {
            return;
        }

        foreach (var pair in _items)
        {
            if (pair.Value.ExpiresAt <= now && _items.TryRemove(pair))
            {
                // Invalidate the tracked minimum so the next queue-full Enqueue re-scans rather
                // than trusting a pointer that may now refer to a removed item. Volatile matches
                // the write pattern used by OnSuccessfulL2Operation and _TryRemoveConflicting.
                Volatile.Write(ref _minExpiryItem, value: null);
                _logger.LogAutoRecoveryItemExpired(pair.Key, pair.Value.Kind);
            }
        }

        foreach (var pair in _items)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var item = pair.Value;
            HybridCacheRecoveryReplayOutcome outcome;

            // Mark the in-flight item so a residual publish enqueued from inside its own replay (post-replay
            // publish failure) is allowed to replace it instead of being subsumed by it.
            Volatile.Write(ref _replaying, item);

            try
            {
                outcome = await item.Replay(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // Single pass at a time (the _processing gate), so the increment needs no interlock.
                item.RetryCount++;

                if (item.RetryCount >= _options.AutoRecoveryMaxRetries)
                {
                    if (_items.TryRemove(pair))
                    {
                        // Invalidate the tracked minimum so the next queue-full Enqueue re-scans rather than
                        // trusting a pointer that may now refer to a removed item.
                        Volatile.Write(ref _minExpiryItem, value: null);
                        _logger.LogAutoRecoveryItemDroppedAfterRetries(exception, item.Key, item.Kind, item.RetryCount);
                    }
                }
                else
                {
                    _logger.LogAutoRecoveryReplayFailed(exception, item.Key, item.Kind, item.RetryCount);
                }

                // Arm the failure barrier so the NEXT pass is delayed (a sustained outage must not turn into a
                // retry storm), but keep processing the rest of THIS pass: a single poison/failing item must
                // not starve healthy items queued behind it. Convergence is timestamp-driven, so replaying the
                // remaining items now is safe regardless of order.
                Volatile.Write(
                    ref _barrierUntilTicks,
                    (_timeProvider.GetUtcNow() + _options.AutoRecoveryDelay).UtcTicks
                );

                continue;
            }
            finally
            {
                Volatile.Write(ref _replaying, value: null);
            }

            // Conditional remove: a newer item (or this item's own residual publish) may have replaced this
            // one mid-replay; keep that one.
            if (_items.TryRemove(pair))
            {
                // Invalidate the tracked minimum so the next queue-full Enqueue re-scans rather than
                // trusting a pointer that may now refer to a removed item.
                Volatile.Write(ref _minExpiryItem, value: null);
            }

            if (outcome == HybridCacheRecoveryReplayOutcome.Replayed)
            {
                _logger.LogAutoRecoveryItemReplayed(item.Key, item.Kind);
            }
            else
            {
                _logger.LogAutoRecoveryItemObsolete(item.Key, item.Kind);
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
        {
            return;
        }

        _disposeCts.Cancel();
        _timer.Dispose();
        _disposeCts.Dispose();
    }

    private void _OnTimerTick()
    {
        if (Volatile.Read(ref _isDisposed) == 1 || _items.IsEmpty)
        {
            return;
        }

        // Acquire the processing gate HERE, before publishing the drain signal. A tick that fires while a prior pass
        // still holds the gate must NOT overwrite _activeProcessTcs: doing so would replace the running pass's signal
        // with a fresh source that this no-op tick completes immediately, letting a concurrent DrainAsync (disposal)
        // observe a completed no-op and return while the real pass is still replaying to L2. The gated pass releases
        // _processing in _RunProcessPassAsync's finally.
        if (Interlocked.CompareExchange(ref _processing, 1, 0) != 0)
        {
            return; // A previous pass is still running and owns the current _activeProcessTcs; leave it in place.
        }

        // Publish the completion signal for the pass we just gated so a concurrent DrainAsync (disposal) can await it.
        // RunContinuationsAsynchronously keeps the awaiting DrainAsync off the thread that completes the source.
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _activeProcessTcs = completion;

        // Fire-and-forget the gated pass. The wrapper runs the gateless core, logs any unexpected fault, releases the
        // _processing gate, and always signals completion so the drain cannot hang.
        _ = _RunProcessPassAsync(completion);
    }

    private async Task _RunProcessPassAsync(TaskCompletionSource completion)
    {
        try
        {
            // The gate was already acquired by _OnTimerTick, so run the gateless core directly — calling the public
            // ProcessAsync here would no-op on the gate this pass already owns.
            await _ProcessCoreAsync(_disposeCts.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // The pass is documented not to throw (e.g. from logger or TimeProvider); log the unexpected fault
            // rather than silently losing it as an unobserved task fault.
            _logger.LogAutoRecoveryProcessUnexpectedFault(exception);
        }
        finally
        {
            // Release the gate acquired in _OnTimerTick, then signal completion so a waiting DrainAsync returns.
            Volatile.Write(ref _processing, 0);
            completion.TrySetResult();
        }
    }

    /// <summary>
    /// Awaits any in-flight <see cref="ProcessAsync"/> task so that <see cref="HybridCache.DisposeAsync"/>
    /// does not return while recovery work is still outstanding. The active task runs with the internal
    /// dispose token which is cancelled before this is called, so it will complete promptly.
    /// </summary>
    internal async Task DrainAsync(CancellationToken cancellationToken)
    {
        var completion = _activeProcessTcs;

        if (completion?.Task.IsCompleted != false)
        {
            return;
        }

        try
        {
            // The source only ever completes successfully (the pass wrapper signals it in a finally and logs any
            // fault itself), so the await can only surface caller cancellation.
            await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Drain timed out or was cancelled — acceptable during shutdown.
        }
    }

    private RecoveryItem? _FindEarliestExpiry()
    {
        // Fast path: the tracked pointer is valid (non-null) — return it without scanning.
        // Called only under _admissionLock, so the pointer read is safe.
        if (_minExpiryItem is not null)
        {
            return _minExpiryItem;
        }

        // Slow path (O(N)): triggered only when the previously-tracked minimum was removed or evicted outside
        // the Enqueue hot path (OnSuccessfulL2Operation, conflict removal, expiry sweep). Re-scan once to find
        // the new minimum and cache it for subsequent queue-full admissions.
        RecoveryItem? earliest = null;

        foreach (var item in _items.Values)
        {
            if (earliest is null || item.ExpiresAt < earliest.ExpiresAt)
            {
                earliest = item;
            }
        }

        _minExpiryItem = earliest;
        return earliest;
    }

    private void _TryRemoveConflicting(KeyValuePair<string, RecoveryItem> pair)
    {
        if (_items.TryRemove(pair))
        {
            // Same conservative invalidation as OnSuccessfulL2Operation: no lock held here, so null to force
            // a re-scan on the next queue-full admission rather than comparing against a potentially stale pointer.
            Volatile.Write(ref _minExpiryItem, value: null);
            _logger.LogAutoRecoveryItemConflicted(pair.Key, pair.Value.Kind);
        }
    }

    private sealed class RecoveryItem(
        string key,
        HybridCacheRecoveryKind kind,
        DateTimeOffset enqueuedAt,
        DateTimeOffset expiresAt,
        Func<CancellationToken, ValueTask<HybridCacheRecoveryReplayOutcome>> replay
    )
    {
        public string Key { get; } = key;

        public HybridCacheRecoveryKind Kind { get; } = kind;

        public DateTimeOffset EnqueuedAt { get; } = enqueuedAt;

        public DateTimeOffset ExpiresAt { get; } = expiresAt;

        public Func<CancellationToken, ValueTask<HybridCacheRecoveryReplayOutcome>> Replay { get; } = replay;

        public int RetryCount { get; set; }
    }
}

internal static partial class HybridCacheRecoveryQueueLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "AutoRecoveryItemQueued",
        Level = LogLevel.Warning,
        Message = "Auto-recovery queued a pending {Kind} for key {Key}; the hybrid cache is in degraded mode until the dependency recovers"
    )]
    public static partial void LogAutoRecoveryItemQueued(this ILogger logger, string key, HybridCacheRecoveryKind kind);

    [LoggerMessage(
        EventId = 2,
        EventName = "AutoRecoveryItemReplayed",
        Level = LogLevel.Information,
        Message = "Auto-recovery replayed the pending {Kind} for key {Key}"
    )]
    public static partial void LogAutoRecoveryItemReplayed(
        this ILogger logger,
        string key,
        HybridCacheRecoveryKind kind
    );

    [LoggerMessage(
        EventId = 3,
        EventName = "AutoRecoveryItemObsolete",
        Level = LogLevel.Debug,
        Message = "Auto-recovery dropped the pending {Kind} for key {Key}: the local entry changed, the queued operation is obsolete"
    )]
    public static partial void LogAutoRecoveryItemObsolete(
        this ILogger logger,
        string key,
        HybridCacheRecoveryKind kind
    );

    [LoggerMessage(
        EventId = 4,
        EventName = "AutoRecoveryItemDroppedAfterRetries",
        Level = LogLevel.Warning,
        Message = "Auto-recovery dropped the pending {Kind} for key {Key} after {RetryCount} failed replay attempts"
    )]
    public static partial void LogAutoRecoveryItemDroppedAfterRetries(
        this ILogger logger,
        Exception exception,
        string key,
        HybridCacheRecoveryKind kind,
        int retryCount
    );

    [LoggerMessage(
        EventId = 5,
        EventName = "AutoRecoveryItemExpired",
        Level = LogLevel.Warning,
        Message = "Auto-recovery dropped the pending {Kind} for key {Key}: the item expired before the dependency recovered"
    )]
    public static partial void LogAutoRecoveryItemExpired(
        this ILogger logger,
        string key,
        HybridCacheRecoveryKind kind
    );

    [LoggerMessage(
        EventId = 6,
        EventName = "AutoRecoveryItemEvicted",
        Level = LogLevel.Warning,
        Message = "Auto-recovery evicted the earliest-expiring pending {Kind} for key {Key} to admit a newer item (queue full)"
    )]
    public static partial void LogAutoRecoveryItemEvicted(
        this ILogger logger,
        string key,
        HybridCacheRecoveryKind kind
    );

    [LoggerMessage(
        EventId = 7,
        EventName = "AutoRecoveryItemRejected",
        Level = LogLevel.Warning,
        Message = "Auto-recovery rejected a pending {Kind} for key {Key}: the queue is full and the item expires before every queued item"
    )]
    public static partial void LogAutoRecoveryItemRejected(
        this ILogger logger,
        string key,
        HybridCacheRecoveryKind kind
    );

    [LoggerMessage(
        EventId = 8,
        EventName = "AutoRecoveryReplayFailed",
        Level = LogLevel.Warning,
        Message = "Auto-recovery replay of the pending {Kind} for key {Key} failed (attempt {RetryCount}); barrier armed before the next pass"
    )]
    public static partial void LogAutoRecoveryReplayFailed(
        this ILogger logger,
        Exception exception,
        string key,
        HybridCacheRecoveryKind kind,
        int retryCount
    );

    [LoggerMessage(
        EventId = 9,
        EventName = "AutoRecoveryItemSuperseded",
        Level = LogLevel.Debug,
        Message = "Auto-recovery dropped the pending {Kind} for key {Key}: a newer L2 operation for the key succeeded"
    )]
    public static partial void LogAutoRecoveryItemSuperseded(
        this ILogger logger,
        string key,
        HybridCacheRecoveryKind kind
    );

    [LoggerMessage(
        EventId = 10,
        EventName = "AutoRecoveryItemConflicted",
        Level = LogLevel.Debug,
        Message = "Auto-recovery dropped the pending {Kind} for key {Key}: a newer invalidation from another instance won the race"
    )]
    public static partial void LogAutoRecoveryItemConflicted(
        this ILogger logger,
        string key,
        HybridCacheRecoveryKind kind
    );

    [LoggerMessage(
        EventId = 11,
        EventName = "AutoRecoveryPublishSubsumed",
        Level = LogLevel.Debug,
        Message = "Auto-recovery dropped an incoming pending PublishInvalidation for key {Key}: the queued {Kind} subsumes it (a successful value-op replay publishes the key invalidation itself)"
    )]
    public static partial void LogAutoRecoveryPublishSubsumed(
        this ILogger logger,
        string key,
        HybridCacheRecoveryKind kind
    );

    [LoggerMessage(
        EventId = 12,
        EventName = "AutoRecoveryProcessUnexpectedFault",
        Level = LogLevel.Error,
        Message = "Auto-recovery background processing faulted unexpectedly; the recovery loop will resume on the next timer tick"
    )]
    public static partial void LogAutoRecoveryProcessUnexpectedFault(this ILogger logger, Exception exception);
}
