// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;

namespace Headless.Messaging.Transactions;

/// <summary>
/// Thread-safe scope-local in-memory work buffer for accumulating work items during a transaction.
/// </summary>
/// <remarks>
/// This buffer stores items in a <see cref="ConcurrentQueue{T}" /> and drains them atomically in
/// registration order. It is not durable: if the process crashes after commit but before the registered
/// commit callback drains the buffer, the buffered items are lost. Consumers that need at-least-once delivery
/// must commit a durable row (outbox or job row) inside the transaction and recover it with their own sweep;
/// this buffer is only the post-commit fast path in front of that store.
/// </remarks>
/// <typeparam name="TWork">The type of work item buffered per transaction.</typeparam>
internal class InMemoryWorkBuffer<TWork>
{
    private readonly ConcurrentQueue<TWork> _items = new();

    /// <summary>
    /// Enqueues a work item. Safe to call concurrently from multiple threads within the same transaction.
    /// </summary>
    /// <param name="work">The work item to buffer.</param>
    public void Add(TWork work)
    {
        _items.Enqueue(work);
    }

    /// <summary>
    /// Dequeues and returns all currently buffered items in the order they were added.
    /// </summary>
    /// <remarks>
    /// Items dequeued here are removed from the buffer; subsequent calls return only items added after this call.
    /// </remarks>
    /// <returns>A snapshot of the drained items, in enqueue order.</returns>
    public IReadOnlyList<TWork> Drain()
    {
        var items = new List<TWork>(_items.Count);

        while (_items.TryDequeue(out var item))
        {
            items.Add(item);
        }

        return items;
    }
}
