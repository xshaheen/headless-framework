// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Headless.Fencing.InMemory;

/// <summary>
/// The in-memory stand-in for a database transaction, kept as unit-local state on one unit of work. It holds every key
/// the unit locked until the unit ends, and keeps the unit's writes to itself until the unit completes, when they
/// replace the committed rows. A rollback or an abandoned unit drops the writes and releases the keys.
/// </summary>
/// <remarks>
/// A key the unit already holds is not locked again, so one unit can fence and then settle, or lock and then admit, the
/// same key. Reads inside the unit see its own uncommitted writes first.
/// </remarks>
internal sealed class UnitRowTransaction<TKey, TRow> : IDisposable
    where TKey : notnull
    where TRow : class
{
    private readonly Lock _gate = new();
    private readonly Dictionary<TKey, IDisposable> _held = [];
    private readonly Dictionary<TKey, TRow?> _staged = [];
    private bool _ended;

    public UnitRowTransaction(IUnitOfWork unitOfWork, InMemoryRowTable<TKey, TRow> table)
    {
        Table = table;

        // Registered when the unit first touches this table, the same shape as the in-memory outbox buffer: the
        // writes join the table only once the unit completes, and a failed unit never runs this.
        unitOfWork.OnCompleted(_CommitAsync);
    }

    public InMemoryRowTable<TKey, TRow> Table { get; }

    /// <summary>Takes the key's lock for the rest of the unit, waiting for any other holder.</summary>
    /// <exception cref="InvalidOperationException">The unit ended while the call waited.</exception>
    public async ValueTask LockAsync(TKey key, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _ThrowIfEnded();

            if (_held.ContainsKey(key))
            {
                return;
            }
        }

        var releaser = await Table.LockAsync(key, cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            if (_ended)
            {
                releaser.Dispose();
                _ThrowIfEnded();
            }

            _held[key] = releaser;
        }
    }

    /// <summary>
    /// Takes the key's lock for the rest of the unit only if no one else holds it, and reports whether the unit now
    /// holds it.
    /// </summary>
    public bool TryLock(TKey key)
    {
        lock (_gate)
        {
            _ThrowIfEnded();

            if (_held.ContainsKey(key))
            {
                return true;
            }

            var releaser = Table.TryLock(key);

            if (releaser is null)
            {
                return false;
            }

            _held[key] = releaser;

            return true;
        }
    }

    /// <summary>Releases a key this unit locked but never wrote, so a skipped candidate is not held needlessly.</summary>
    public void Unlock(TKey key)
    {
        lock (_gate)
        {
            if (!_staged.ContainsKey(key) && _held.Remove(key, out var releaser))
            {
                releaser.Dispose();
            }
        }
    }

    /// <summary>Reads the row as this unit sees it: its own uncommitted write first, then the committed row.</summary>
    public TRow? Read(TKey key)
    {
        lock (_gate)
        {
            return _staged.TryGetValue(key, out var staged) ? staged : Table.Read(key);
        }
    }

    /// <summary>Records a write, or a delete for <see langword="null" />, on a key this unit holds.</summary>
    public void Stage(TKey key, TRow? row)
    {
        lock (_gate)
        {
            _ThrowIfEnded();

            if (!_held.ContainsKey(key))
            {
                throw new InvalidOperationException("An in-memory write must hold its key's lock first.");
            }

            _staged[key] = row;
        }
    }

    /// <summary>Drops the uncommitted writes and releases every key; the unit-local state's end on either outcome.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _ended = true;
            _staged.Clear();
            _ReleaseAll();
        }
    }

    private ValueTask _CommitAsync()
    {
        lock (_gate)
        {
            foreach (var (key, row) in _staged)
            {
                Table.Write(key, row);
            }

            _staged.Clear();

            // Released here rather than when the unit disposes its state after the whole completion drain: another
            // completion callback waiting on one of these keys would otherwise wait on this drain forever.
            _ended = true;
            _ReleaseAll();
        }

        return ValueTask.CompletedTask;
    }

    private void _ReleaseAll()
    {
        foreach (var releaser in _held.Values)
        {
            releaser.Dispose();
        }

        _held.Clear();
    }

    private void _ThrowIfEnded()
    {
        if (_ended)
        {
            throw new InvalidOperationException(
                "The unit of work already ended, so its in-memory writes can no longer be staged or locked."
            );
        }
    }
}
