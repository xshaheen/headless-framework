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
    private readonly List<WaiterEntry> _waiters = [];
    private readonly Lock _waitersLock = new();

    /// <summary>Gets all published messages recorded so far.</summary>
    public IReadOnlyCollection<RecordedMessage> Published => _published.ToArray();

    /// <summary>Gets all consumed messages recorded so far.</summary>
    public IReadOnlyCollection<RecordedMessage> Consumed => _consumed.ToArray();

    /// <summary>Gets all faulted messages recorded so far.</summary>
    public IReadOnlyCollection<RecordedMessage> Faulted => _faulted.ToArray();

    /// <summary>Records a message and signals any waiting tasks that match.</summary>
    public void Record(RecordedMessage message, MessageObservationType type)
    {
        var queue = _GetQueue(type);
        queue.Enqueue(message);

        lock (_waitersLock)
        {
            for (var i = _waiters.Count - 1; i >= 0; i--)
            {
                var waiter = _waiters[i];

                if (
                    waiter.Type == type
                    && waiter.MessageType.IsAssignableFrom(message.MessageType)
                    && (waiter.Predicate == null || waiter.Predicate(message.Message))
                )
                {
                    if (waiter.Tcs.TrySetResult(message))
                    {
                        _waiters.RemoveAt(i);
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
    /// <param name="predicate">Optional additional filter applied to the deserialized payload.</param>
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

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

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
            lock (_waitersLock)
            {
                _waiters.Remove(entry);
            }

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

        lock (_waitersLock)
        {
            foreach (var waiter in _waiters)
            {
                waiter.Tcs.TrySetCanceled();
            }

            _waiters.Clear();
        }
    }

    private RecordedMessage? _FindExisting(Type messageType, MessageObservationType type, Func<object, bool>? predicate)
    {
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
