// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;

namespace Headless.CommitCoordination;

/// <summary>
/// Thread-safe scope-local in-memory work buffer.
/// </summary>
/// <typeparam name="TWork">The buffered work type.</typeparam>
[PublicAPI]
public class InMemoryWorkBuffer<TWork> : ICommitWorkBuffer
{
    private readonly ConcurrentQueue<TWork> _items = new();

    /// <summary>
    /// Adds work to the buffer.
    /// </summary>
    /// <param name="work">The work item.</param>
    public void Add(TWork work)
    {
        _items.Enqueue(work);
    }

    /// <summary>
    /// Drains a snapshot of currently buffered work.
    /// </summary>
    /// <returns>The drained work items.</returns>
    public IReadOnlyList<TWork> Drain()
    {
        var items = new List<TWork>();

        while (_items.TryDequeue(out var item))
        {
            items.Add(item);
        }

        return items;
    }
}
