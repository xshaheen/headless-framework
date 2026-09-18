// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;

namespace Headless.Messaging.Testing;

/// <summary>
/// Thread-safe store that records messages observed by the test harness and provides
/// signal-based async waiting for message observation.
/// </summary>
internal sealed class MessageObservationStore(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentQueue<RecordedMessage> _published = new();
    private readonly ConcurrentQueue<RecordedMessage> _consumed = new();
    private readonly ConcurrentQueue<RecordedMessage> _faulted = new();
    private readonly ConcurrentQueue<RecordedMessage> _exhausted = new();
    private readonly ConcurrentDictionary<
        (Type, MessageObservationType, MessageLane),
        ConcurrentQueue<RecordedMessage>
    > _typeIndex = [];
    private readonly ConcurrentDictionary<
        (Type, MessageObservationType),
        ConcurrentQueue<RecordedMessage>
    > _typeOnlyIndex = [];
    private readonly List<WaiterEntry> _waiters = [];
    private readonly Lock _waitersLock = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _publishedArrivals = new(
        StringComparer.Ordinal
    );
    private long _generation;

    /// <summary>
    /// Counts the resets this store has seen. The recording transports stamp it on every send so the recording
    /// consume pipeline can tell a message from the current round apart from one sent before the last
    /// <see cref="Clear"/>, whose Published record no longer exists.
    /// </summary>
    public long Generation => Volatile.Read(ref _generation);

    /// <summary>Gets all published messages recorded so far. Each access allocates a snapshot array.</summary>
    public IReadOnlyCollection<RecordedMessage> Published => _published.ToArray();

    /// <summary>Gets all consumed messages recorded so far. Each access allocates a snapshot array.</summary>
    public IReadOnlyCollection<RecordedMessage> Consumed => _consumed.ToArray();

    /// <summary>Gets all faulted messages recorded so far. Each access allocates a snapshot array.</summary>
    public IReadOnlyCollection<RecordedMessage> Faulted => _faulted.ToArray();

    /// <summary>Gets all exhausted messages recorded so far. Each access allocates a snapshot array.</summary>
    public IReadOnlyCollection<RecordedMessage> Exhausted => _exhausted.ToArray();

    /// <summary>Records a message and signals any waiting tasks that match.</summary>
    public void Record(RecordedMessage message, MessageObservationType type)
    {
        var queue = _GetQueue(type);
        queue.Enqueue(message);
        _typeIndex.GetOrAdd((message.MessageType, type, message.Lane), static _ => new()).Enqueue(message);
        _typeOnlyIndex.GetOrAdd((message.MessageType, type), static _ => new()).Enqueue(message);

        if (type is MessageObservationType.Published && message.MessageId.Length > 0)
        {
            // GetOrAdd, not TryGetValue: the send thread records Published while the listener thread races to start
            // its wait, so record-before-waiter is a common interleaving — memoizing the completion makes that waiter
            // return instantly. TryGetValue-only would strand it for the full budget instead.
            _publishedArrivals.GetOrAdd(message.MessageId, static _ => _CreateArrival()).TrySetResult();
        }

        // Snapshot candidates under lock, evaluate predicates outside to avoid
        // holding the lock during potentially expensive user predicates.
        List<WaiterEntry>? candidates;

        lock (_waitersLock)
        {
            candidates = _waiters.Count == 0 ? null : [.. _waiters];
        }

        if (candidates is null)
        {
            return;
        }

        foreach (var waiter in candidates)
        {
            if (
                waiter.Type == type
                && (waiter.Lane is null || waiter.Lane == message.Lane)
                && waiter.MessageType.IsAssignableFrom(message.MessageType)
                && (waiter.Predicate == null || waiter.Predicate(message.Message))
            )
            {
                if (waiter.Tcs.TrySetResult(message))
                {
                    lock (_waitersLock)
                    {
                        _waiters.Remove(waiter);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Waits asynchronously for a message of the specified type and observation kind to be recorded.
    /// Returns immediately if a matching message already exists in the store.
    /// </summary>
    /// <param name="messageType">The CLR type to match (assignability check).</param>
    /// <param name="type">The observation bucket to search.</param>
    /// <param name="predicate">Optional additional filter applied to the deserialized payload. Must be side-effect-free.</param>
    /// <param name="timeout">Maximum time to wait before throwing <see cref="MessageObservationTimeoutException"/>.</param>
    /// <param name="cancellationToken">Token to cancel the wait (propagates as <see cref="OperationCanceledException"/>).</param>
    /// <returns>The first matching <see cref="RecordedMessage"/>.</returns>
    /// <exception cref="MessageObservationTimeoutException">Thrown when <paramref name="timeout"/> elapses without a match.</exception>
    public async Task<RecordedMessage> WaitForAsync(
        Type messageType,
        MessageObservationType type,
        MessageLane? lane,
        Func<object, bool>? predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
    {
        var tcs = new TaskCompletionSource<RecordedMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Check existing messages first before registering a waiter (fast path)
        var existing = _FindExisting(messageType, type, lane, predicate);
        if (existing != null)
        {
            return existing;
        }

        var entry = new WaiterEntry(messageType, type, lane, predicate, tcs);

        // Create CTS before registering waiter to ensure timeout is armed before Record() can signal.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        lock (_waitersLock)
        {
            // Double-check after acquiring lock to avoid a race with Record()
            existing = _FindExisting(messageType, type, lane, predicate);
            if (existing != null)
            {
                return existing;
            }

            _waiters.Add(entry);
        }

        var startTime = _timeProvider.GetUtcNow();

        try
        {
            // The registration is disposed when this `using` block exits (line below), which always happens before
            // the outer `using var cts` disposes in the finally, so the callback cannot fire on a disposed CTS.
            // ReSharper disable once AccessToDisposedClosure
            await using (cts.Token.Register(() => tcs.TrySetCanceled(cts.Token)))
            {
                return await tcs.Task.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout — not cancelled externally
            var elapsed = _timeProvider.GetUtcNow() - startTime;
            var observed = _GetQueue(type).ToArray();
            throw new MessageObservationTimeoutException(
                messageType,
                type,
                elapsed,
                observed,
                hasPredicate: predicate is not null || lane is not null
            );
        }
        finally
        {
            lock (_waitersLock)
            {
                _waiters.Remove(entry);
            }
        }
    }

    /// <summary>
    /// Completes once the Published observation for <paramref name="messageId"/> has been recorded, or once
    /// <paramref name="timeout"/> elapses. The in-memory transport hands a message to its consumer inside the send,
    /// before the sending thread records Published, so the consume pipeline awaits this to keep a message's Published
    /// observation ahead of its Consumed or Faulted one.
    /// </summary>
    public async Task WaitForPublishedRecordAsync(
        string messageId,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        var arrival = _publishedArrivals.GetOrAdd(messageId, static _ => _CreateArrival());

        try
        {
            await arrival.Task.WaitAsync(timeout, _timeProvider, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // The consumer must still run; a Published record that never arrives shows up in the test's own assertions.
        }
        finally
        {
            // Reclaim this handshake's entry on every exit so the map cannot grow across waits — completed or not,
            // the pair-form TryRemove only matches this exact instance, so a concurrent waiter's fresh signal is
            // never stolen. A later waiter for an already-recorded id GetOrAdds a fresh TCS that will not complete;
            // it runs out its budget and the wait is swallowed — the same path as a record that never arrives, and
            // the waiter-side GetOrAdd above only runs once per consume, so the leak stays bounded per round.
            _publishedArrivals.TryRemove(new KeyValuePair<string, TaskCompletionSource>(messageId, arrival));
        }
    }

    /// <summary>Clears all recorded messages and cancels pending waiters.</summary>
    public void Clear()
    {
        Interlocked.Increment(ref _generation);

        // A consumer already waiting for a Published record belongs to the round being cleared; release it now so the
        // consumer thread is not held for the full wait budget by a record that can no longer arrive.
        foreach (var arrival in _publishedArrivals.Values)
        {
            arrival.TrySetResult();
        }

        _publishedArrivals.Clear();

        while (_published.TryDequeue(out _)) { }

        while (_consumed.TryDequeue(out _)) { }

        while (_faulted.TryDequeue(out _)) { }

        while (_exhausted.TryDequeue(out _)) { }

        _typeIndex.Clear();
        _typeOnlyIndex.Clear();

        lock (_waitersLock)
        {
            foreach (var waiter in _waiters)
            {
                waiter.Tcs.TrySetException(
                    new InvalidOperationException(
                        "MessagingTestHarness.ResetAsync() was called while a WaitFor* operation was pending."
                    )
                );
            }

            _waiters.Clear();
        }
    }

    private static TaskCompletionSource _CreateArrival()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private RecordedMessage? _FindExisting(
        Type messageType,
        MessageObservationType type,
        MessageLane? lane,
        Func<object, bool>? predicate
    )
    {
        // Fast path: exact type match avoids scanning all messages
        if (
            lane is { } concreteMessageLane
            && _typeIndex.TryGetValue((messageType, type, concreteMessageLane), out var indexed)
        )
        {
            var match = indexed.FirstOrDefault(m => predicate == null || predicate(m.Message));
            if (match != null)
            {
                return match;
            }
        }
        else if (lane is null && _typeOnlyIndex.TryGetValue((messageType, type), out var typeIndexed))
        {
            var match = typeIndexed.FirstOrDefault(m => predicate == null || predicate(m.Message));
            if (match != null)
            {
                return match;
            }
        }

        // Assignability scan for polymorphic queries and index-race fallback
        return _GetQueue(type)
            .FirstOrDefault(m =>
                (lane is null || lane == m.Lane)
                && messageType.IsAssignableFrom(m.MessageType)
                && (predicate == null || predicate(m.Message))
            );
    }

    private ConcurrentQueue<RecordedMessage> _GetQueue(MessageObservationType type)
    {
        return type switch
        {
            MessageObservationType.Published => _published,
            MessageObservationType.Consumed => _consumed,
            MessageObservationType.Faulted => _faulted,
            MessageObservationType.Exhausted => _exhausted,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, message: null),
        };
    }

    internal DateTimeOffset GetUtcNow()
    {
        return _timeProvider.GetUtcNow();
    }

    private sealed record WaiterEntry(
        Type MessageType,
        MessageObservationType Type,
        MessageLane? Lane,
        Func<object, bool>? Predicate,
        TaskCompletionSource<RecordedMessage> Tcs
    );
}
