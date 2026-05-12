// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;

namespace Headless.Messaging.Testing;

/// <summary>
/// Thread-safe store that records messages observed by the test harness and provides
/// signal-based async waiting for message observation.
/// </summary>
internal sealed class MessageObservationStore
{
    private readonly ConcurrentQueue<RecordedMessage> _published = new();
    private readonly ConcurrentQueue<RecordedMessage> _consumed = new();
    private readonly ConcurrentQueue<RecordedMessage> _faulted = new();
    private readonly ConcurrentDictionary<(Type, MessageObservationType), ConcurrentQueue<RecordedMessage>> _typeIndex =
        new();
    private readonly List<WaiterEntry> _waiters = [];
    private readonly Lock _waitersLock = new();

    /// <summary>Gets all published messages recorded so far. Each access allocates a snapshot array.</summary>
    public IReadOnlyCollection<RecordedMessage> Published => _published.ToArray();

    /// <summary>Gets all consumed messages recorded so far. Each access allocates a snapshot array.</summary>
    public IReadOnlyCollection<RecordedMessage> Consumed => _consumed.ToArray();

    /// <summary>Gets all faulted messages recorded so far. Each access allocates a snapshot array.</summary>
    public IReadOnlyCollection<RecordedMessage> Faulted => _faulted.ToArray();

    /// <summary>Records a message and signals any waiting tasks that match.</summary>
    public void Record(RecordedMessage message, MessageObservationType type)
    {
        var queue = _GetQueue(type);
        queue.Enqueue(message);
        _typeIndex.GetOrAdd((message.MessageType, type), static _ => new()).Enqueue(message);

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
        Func<object, bool>? predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    )
    {
        var tcs = new TaskCompletionSource<RecordedMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Check existing messages first before registering a waiter (fast path)
        var existing = _FindExisting(messageType, type, predicate);
        if (existing != null)
        {
            return existing;
        }

        var entry = new WaiterEntry(messageType, type, predicate, tcs);

        // Create CTS before registering waiter to ensure timeout is armed before Record() can signal.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        lock (_waitersLock)
        {
            // Double-check after acquiring lock to avoid a race with Record()
            existing = _FindExisting(messageType, type, predicate);
            if (existing != null)
            {
                return existing;
            }

            _waiters.Add(entry);
        }

        var startTime = DateTimeOffset.UtcNow;

        try
        {
            using (cts.Token.Register(() => tcs.TrySetCanceled(cts.Token)))
            {
                return await tcs.Task.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout — not cancelled externally
            var elapsed = DateTimeOffset.UtcNow - startTime;
            var observed = _GetQueue(type).ToArray();
            throw new MessageObservationTimeoutException(
                messageType,
                type,
                elapsed,
                observed,
                hasPredicate: predicate is not null
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

    /// <summary>Clears all recorded messages and cancels pending waiters.</summary>
    public void Clear()
    {
        while (_published.TryDequeue(out _)) { }

        while (_consumed.TryDequeue(out _)) { }

        while (_faulted.TryDequeue(out _)) { }

        _typeIndex.Clear();

        lock (_waitersLock)
        {
            foreach (var waiter in _waiters)
            {
                waiter.Tcs.TrySetException(
                    new InvalidOperationException(
                        "MessagingTestHarness.Clear() was called while a WaitFor* operation was pending."
                    )
                );
            }

            _waiters.Clear();
        }
    }

    private RecordedMessage? _FindExisting(Type messageType, MessageObservationType type, Func<object, bool>? predicate)
    {
        // Fast path: exact type match avoids scanning all messages
        if (_typeIndex.TryGetValue((messageType, type), out var indexed))
        {
            var match = indexed.FirstOrDefault(m => predicate == null || predicate(m.Message));
            if (match != null)
            {
                return match;
            }
        }

        // Assignability scan for polymorphic queries and index-race fallback
        return _GetQueue(type)
            .FirstOrDefault(m =>
                messageType.IsAssignableFrom(m.MessageType) && (predicate == null || predicate(m.Message))
            );
    }

    private ConcurrentQueue<RecordedMessage> _GetQueue(MessageObservationType type) =>
        type switch
        {
            MessageObservationType.Published => _published,
            MessageObservationType.Consumed => _consumed,
            MessageObservationType.Faulted => _faulted,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };

    private sealed record WaiterEntry(
        Type MessageType,
        MessageObservationType Type,
        Func<object, bool>? Predicate,
        TaskCompletionSource<RecordedMessage> Tcs
    );
}
