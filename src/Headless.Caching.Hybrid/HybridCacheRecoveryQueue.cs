// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Headless.Caching;

/// <summary>Kind of pending operation tracked by <see cref="HybridCacheRecoveryQueue"/>.</summary>
internal enum HybridCacheRecoveryKind
{
    SetEntry,
    Remove,
    PublishInvalidation,
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
/// reference: FusionCache's AutoRecoveryService, adapted). One pending operation per cache key — a newer
/// operation for the same key replaces the older one, and any successful L2 write for a key clears its pending
/// item, so a surviving item always represents the latest local intent for that key.
/// </summary>
/// <remarks>
/// Scope is intentionally limited to single-key operations (factory/entry sets, scalar upserts, removes, and
/// their invalidation publishes). Bulk, atomic (increment/set-if), and set operations are not captured: their
/// failure semantics are value-dependent and replaying them later could double-apply effects.
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

    /// <summary>Whether a pending item exists for the given key (test/diagnostic hook).</summary>
    internal bool Contains(string key) => _items.ContainsKey(key);

    /// <summary>
    /// Default retention window for items without a natural expiry (removes, publishes, sets without TTL).
    /// </summary>
    internal TimeSpan DefaultRetention => _options.AutoRecoveryDelay * _options.AutoRecoveryMaxRetries;

    /// <summary>
    /// Queues a pending operation for the key. A newer operation for the same key replaces the older one. On
    /// overflow the item with the earliest expiry (including the incoming one) is sacrificed to keep the items
    /// most likely to still matter when the dependency recovers.
    /// </summary>
    public void Enqueue(
        string key,
        HybridCacheRecoveryKind kind,
        DateTimeOffset expiresAt,
        Func<CancellationToken, ValueTask<HybridCacheRecoveryReplayOutcome>> replay
    )
    {
        var now = _timeProvider.GetUtcNow();

        if (expiresAt <= now)
        {
            return; // Nothing left to recover.
        }

        var item = new RecoveryItem(key, kind, now, expiresAt, replay);

        lock (_admissionLock)
        {
            if (!_items.ContainsKey(key) && _items.Count >= _options.AutoRecoveryMaxItems)
            {
                var victim = _FindEarliestExpiry();

                if (victim is not null && victim.ExpiresAt < expiresAt)
                {
                    _items.TryRemove(victim.Key, out _);
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

        bool IsOlder(RecoveryItem item) => message.Timestamp is null || item.EnqueuedAt < message.Timestamp.Value;

        if (message.FlushAll)
        {
            foreach (var pair in _items)
            {
                if (IsOlder(pair.Value))
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
                if (pair.Key.StartsWith(message.Prefix, StringComparison.Ordinal) && IsOlder(pair.Value))
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
                if (_items.TryGetValue(key, out var item) && IsOlder(item))
                {
                    _TryRemoveConflicting(new(key, item));
                }
            }

            return;
        }

        if (!string.IsNullOrEmpty(message.Key) && _items.TryGetValue(message.Key, out var single) && IsOlder(single))
        {
            _TryRemoveConflicting(new(message.Key, single));
        }
    }

    /// <summary>
    /// Runs one processing pass: skipped while behind the failure barrier, drops expired items, then replays
    /// pending items oldest-first. The pass stops at the first replay failure and arms the barrier so a
    /// sustained outage does not turn into a retry storm. Never throws (cancellation included).
    /// </summary>
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _processing, 1, 0) != 0)
        {
            return; // A previous pass is still running; the timer fires again next cadence.
        }

        try
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
                    _logger.LogAutoRecoveryItemExpired(pair.Key, pair.Value.Kind);
                }
            }

            foreach (var pair in _items.ToArray().OrderBy(static p => p.Value.EnqueuedAt))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                var item = pair.Value;
                HybridCacheRecoveryReplayOutcome outcome;

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
                            _logger.LogAutoRecoveryItemDroppedAfterRetries(
                                exception,
                                item.Key,
                                item.Kind,
                                item.RetryCount
                            );
                        }
                    }
                    else
                    {
                        _logger.LogAutoRecoveryReplayFailed(exception, item.Key, item.Kind, item.RetryCount);
                    }

                    Volatile.Write(
                        ref _barrierUntilTicks,
                        (_timeProvider.GetUtcNow() + _options.AutoRecoveryDelay).UtcTicks
                    );

                    return;
                }

                // Conditional remove: a newer item may have replaced this one mid-replay; keep that one.
                _items.TryRemove(pair);

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
        finally
        {
            Volatile.Write(ref _processing, 0);
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

        // Fire-and-forget: ProcessAsync never throws, and the _processing gate prevents overlapping passes.
        _ = ProcessAsync(_disposeCts.Token);
    }

    private RecoveryItem? _FindEarliestExpiry()
    {
        RecoveryItem? earliest = null;

        foreach (var item in _items.Values)
        {
            if (earliest is null || item.ExpiresAt < earliest.ExpiresAt)
            {
                earliest = item;
            }
        }

        return earliest;
    }

    private void _TryRemoveConflicting(KeyValuePair<string, RecoveryItem> pair)
    {
        if (_items.TryRemove(pair))
        {
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
        Level = LogLevel.Debug,
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
}
