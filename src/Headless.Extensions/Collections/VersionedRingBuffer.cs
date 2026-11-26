// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Collections;

/// <summary>
/// A thread-safe, bounded, in-memory buffer that keeps the newest <see cref="Capacity"/> items and counts every change,
/// so a reader can wait for the next change instead of polling the whole list.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <remarks>
/// <para>
/// Every add, remove, and clear that changes the buffer increments <see cref="Version"/>. An add passes the new version
/// to the item factory as the item's sequence, so sequences increase strictly in insertion order and are the ordering
/// key: a reader that remembers the last version it saw can ask for the items added after it.
/// </para>
/// <para>
/// The factory and every predicate run while the buffer holds its lock. Keep them short, and never call back into
/// the buffer from them. Reading a clock inside the factory makes timestamps follow the sequence order.
/// </para>
/// </remarks>
/// <param name="capacity">The maximum number of items to keep. Must be positive.</param>
/// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is zero or negative.</exception>
[PublicAPI]
public sealed class VersionedRingBuffer<T>(int capacity)
    where T : class
{
    private readonly Lock _lock = new();

    // Oldest first. A list rather than a circular array, because Remove takes an item out of the middle; the capacity
    // keeps the shifts small.
    private readonly List<T> _items = [];
    private long _version;
    private TaskCompletionSource<long> _changed = _NewSignal();

    /// <summary>Gets the maximum number of items the buffer keeps.</summary>
    public int Capacity { get; } = Argument.IsPositive(capacity);

    /// <summary>Gets the number of items the buffer holds now.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _items.Count;
            }
        }
    }

    /// <summary>Gets the change counter, which increases on every add, and on every remove or clear that removes an item.</summary>
    public long Version
    {
        get
        {
            lock (_lock)
            {
                return _version;
            }
        }
    }

    /// <summary>
    /// Creates an item with the next sequence and adds it, dropping the oldest item when the buffer is full.
    /// </summary>
    /// <param name="create">
    /// Creates the item from its sequence, which is the <see cref="Version"/> after the add. Runs under the lock.
    /// </param>
    /// <returns>The added item.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="create"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="create"/> returned <see langword="null"/>.</exception>
    /// <remarks>When <paramref name="create"/> throws, the buffer and its version stay unchanged.</remarks>
    public T Add(Func<long, T> create)
    {
        Argument.IsNotNull(create);

        T item;
        TaskCompletionSource<long> signal;
        long version;

        lock (_lock)
        {
            item = create(_version + 1) ?? throw new InvalidOperationException("The item factory returned null.");
            version = ++_version;
            _items.Add(item);

            if (_items.Count > Capacity)
            {
                _items.RemoveAt(0);
            }

            signal = _SwapSignal();
        }

        signal.TrySetResult(version);

        return item;
    }

    /// <summary>Lists the items that match <paramref name="match"/>, newest first.</summary>
    /// <param name="match">The filter, or <see langword="null"/> to list every item. Runs under the lock.</param>
    /// <param name="limit">The maximum number of items to return, or <see langword="null"/> for no limit.</param>
    /// <returns>A snapshot of the matching items.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> is zero or negative.</exception>
    public IReadOnlyList<T> ToListNewestFirst(Func<T, bool>? match, int? limit)
    {
        if (limit is { } max)
        {
            Argument.IsPositive(max, paramName: nameof(limit));
        }

        var result = new List<T>();

        lock (_lock)
        {
            for (var i = _items.Count - 1; i >= 0 && result.Count != limit; i--)
            {
                var item = _items[i];

                if (match is null || match(item))
                {
                    result.Add(item);
                }
            }
        }

        return result;
    }

    /// <summary>Gets the newest item that matches <paramref name="match"/>.</summary>
    /// <param name="match">The filter. Runs under the lock.</param>
    /// <returns>The item, or <see langword="null"/> when none matches.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="match"/> is <see langword="null"/>.</exception>
    public T? Find(Func<T, bool> match)
    {
        Argument.IsNotNull(match);

        lock (_lock)
        {
            for (var i = _items.Count - 1; i >= 0; i--)
            {
                if (match(_items[i]))
                {
                    return _items[i];
                }
            }

            return null;
        }
    }

    /// <summary>Removes the newest item that matches <paramref name="match"/>.</summary>
    /// <param name="match">The filter. Runs under the lock.</param>
    /// <returns><see langword="true"/> when an item was removed; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="match"/> is <see langword="null"/>.</exception>
    public bool Remove(Func<T, bool> match)
    {
        Argument.IsNotNull(match);

        TaskCompletionSource<long> signal;
        long version;

        lock (_lock)
        {
            var index = _items.FindLastIndex(item => match(item));

            if (index < 0)
            {
                return false;
            }

            _items.RemoveAt(index);
            version = ++_version;
            signal = _SwapSignal();
        }

        signal.TrySetResult(version);

        return true;
    }

    /// <summary>Removes every item. The version changes only when the buffer held an item.</summary>
    /// <returns>The number of items removed.</returns>
    public int Clear()
    {
        TaskCompletionSource<long> signal;
        long version;
        int removed;

        lock (_lock)
        {
            removed = _items.Count;

            if (removed == 0)
            {
                return 0;
            }

            _items.Clear();
            version = ++_version;
            signal = _SwapSignal();
        }

        signal.TrySetResult(version);

        return removed;
    }

    /// <summary>
    /// Waits until <see cref="Version"/> differs from <paramref name="knownVersion"/>, and returns at once when it
    /// already does.
    /// </summary>
    /// <param name="knownVersion">The version the caller last read.</param>
    /// <param name="cancellationToken">A token to stop waiting.</param>
    /// <returns>The current version.</returns>
    /// <exception cref="OperationCanceledException">The wait was canceled.</exception>
    public async ValueTask<long> WaitForChangeAsync(long knownVersion, CancellationToken cancellationToken = default)
    {
        Task<long> changed;

        lock (_lock)
        {
            if (_version != knownVersion)
            {
                return _version;
            }

            changed = _changed.Task;
        }

        return await changed.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    // Called under the lock: the waiters of the old signal are completed after the lock is released.
    private TaskCompletionSource<long> _SwapSignal()
    {
        var previous = _changed;
        _changed = _NewSignal();

        return previous;
    }

    private static TaskCompletionSource<long> _NewSignal()
    {
        // Waiters resume off the thread that completes the signal, so a writer never runs a reader's continuation.
        return new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
