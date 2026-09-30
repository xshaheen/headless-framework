// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Threading;
using Headless.UnitOfWork;

namespace Headless.Fencing.InMemory;

/// <summary>
/// A process-local table of immutable rows with one exclusive lock per key, the in-memory stand-in for a table whose
/// rows are locked for update. Readers get the stored row instance, which nobody mutates, so every read is a snapshot.
/// </summary>
internal sealed class InMemoryRowTable<TKey, TRow>(Func<TKey, string> lockName) : IDisposable
    where TKey : notnull
    where TRow : class
{
    private readonly KeyedAsyncLock _locks = new();

    /// <summary>Gets the committed rows. Writers replace a row only while holding its key's lock.</summary>
    public ConcurrentDictionary<TKey, TRow> Rows { get; } = new();

    /// <summary>Waits for and takes the key's lock; disposing the result releases it.</summary>
    public Task<IDisposable> LockAsync(TKey key, CancellationToken cancellationToken)
    {
        return _locks.LockAsync(lockName(key), cancellationToken);
    }

    /// <summary>Takes the key's lock only if no one holds it, the in-memory form of <c>SKIP LOCKED</c>.</summary>
    public IDisposable? TryLock(TKey key)
    {
        return _locks.TryLock(lockName(key));
    }

    /// <summary>Reads the committed row, or <see langword="null" /> when the key has none.</summary>
    public TRow? Read(TKey key)
    {
        return Rows.TryGetValue(key, out var row) ? row : null;
    }

    /// <summary>Replaces the committed row, or deletes it when <paramref name="row" /> is <see langword="null" />.</summary>
    public void Write(TKey key, TRow? row)
    {
        if (row is null)
        {
            Rows.TryRemove(key, out _);
        }
        else
        {
            Rows[key] = row;
        }
    }

    /// <summary>Returns the transaction <paramref name="unitOfWork" /> runs against this table, starting it if new.</summary>
    /// <exception cref="InvalidOperationException">The unit already runs against another table of the same kind.</exception>
    public UnitRowTransaction<TKey, TRow> Join(IUnitOfWork unitOfWork)
    {
        var transaction = unitOfWork.GetOrAdd(
            this,
            static (unit, table) => new UnitRowTransaction<TKey, TRow>(unit, table)
        );

        if (!ReferenceEquals(transaction.Table, this))
        {
            throw new InvalidOperationException(
                "The unit of work already holds rows of another in-memory store of the same kind; one unit can "
                    + "write to only one in-memory store per kind."
            );
        }

        return transaction;
    }

    public void Dispose()
    {
        _locks.Dispose();
    }
}
