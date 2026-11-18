// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Headless.LiveQueries.Internal;

/// <summary>What one <c>SaveChanges</c> call that has not returned yet is writing.</summary>
/// <param name="contextId">
/// The context instance and pool lease the save runs on. A pooled context that is leased again carries a new lease,
/// which is how a capture left behind by a save that never reported its end is told from the current one.
/// </param>
/// <param name="failed">Counts and logs a step that threw and was contained.</param>
internal sealed class LiveSaveCapture(DbContextId contextId, Action<Exception> failed)
{
    private readonly Lock _lock = new();

    // Tables announced whole.
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

    // Rows announced one by one, and their tables: the save's commands name those tables too, and that must not
    // turn them into whole tables.
    private List<LivePendingRow>? _rows;
    private HashSet<string>? _rowKeys;

    private ChangeTracker? _watched;
    private Func<IEntityType, string[]>? _keysOf;
    private Func<IEntityType, string[]>? _dependentKeysOf;
    private bool _transactionStarted;
    private bool _commandSucceeded;
    private bool _announced;
    private bool _announcedForTransaction;

    public DbContextId ContextId { get; } = contextId;

    public void Add(IEnumerable<string> keys)
    {
        lock (_lock)
        {
            _keys.UnionWith(keys);
        }
    }

    /// <summary>
    /// Keeps the rows the change tracker held when the save began, and watches the change tracker until the save
    /// sends its first command: an entity that another interceptor, a cascade or change detection adds or changes
    /// in between is written by this save without being among the rows, so its tables are announced whole.
    /// </summary>
    /// <param name="rows">The rows; not empty.</param>
    /// <param name="changeTracker">The change tracker of the saving context.</param>
    /// <param name="keysOf">Returns the keys of every table an entity type is stored in.</param>
    /// <param name="dependentKeysOf">
    /// Returns the keys of every table whose rows the database removes or clears when a row of an entity type is
    /// deleted.
    /// </param>
    public void AddRows(
        List<LivePendingRow> rows,
        ChangeTracker changeTracker,
        Func<IEntityType, string[]> keysOf,
        Func<IEntityType, string[]> dependentKeysOf
    )
    {
        lock (_lock)
        {
            _rows = rows;
            _rowKeys = new HashSet<string>(rows.Select(row => row.Key), StringComparer.Ordinal);
            _keysOf = keysOf;
            _dependentKeysOf = dependentKeysOf;
            _watched = changeTracker;
        }

        changeTracker.Tracked += _OnTracked;
        changeTracker.StateChanged += _OnStateChanged;
    }

    /// <summary>
    /// Stops watching the change tracker. From the first command on, the entities the save writes are settled, and
    /// the state changes that follow are the save accepting them.
    /// </summary>
    public void StopWatching()
    {
        ChangeTracker? watched;

        lock (_lock)
        {
            watched = _watched;
            _watched = null;
        }

        if (watched is not null)
        {
            watched.Tracked -= _OnTracked;
            watched.StateChanged -= _OnStateChanged;
        }
    }

    /// <summary>Records that EF Core began a transaction during the save, which is the save's own.</summary>
    public void TransactionStarted()
    {
        lock (_lock)
        {
            _transactionStarted = true;

            // A new transaction is a new try of the save, whose commit must be announced again.
            _announcedForTransaction = false;
        }
    }

    /// <summary>Records that one of the save's commands ran to the end.</summary>
    public void CommandSucceeded(IEnumerable<string> keys)
    {
        lock (_lock)
        {
            foreach (var key in keys)
            {
                if (_rowKeys?.Contains(key) is not true)
                {
                    _keys.Add(key);
                }
            }

            _commandSucceeded = true;
        }
    }

    /// <summary>
    /// Returns the writes for an announcement made because the save's own transaction committed, and none when
    /// that transaction's end was already announced: a commit that throws after it was applied reports its end
    /// twice.
    /// </summary>
    public LiveWriteSet TakeForCommit()
    {
        StopWatching();

        lock (_lock)
        {
            if (_announcedForTransaction)
            {
                return LiveWriteSet.Empty;
            }

            _announced = true;
            _announcedForTransaction = true;

            return _Writes(matchRows: true);
        }
    }

    /// <summary>
    /// Returns the writes still to route when the save returned normally: none when its own transaction already
    /// announced them.
    /// </summary>
    public LiveWriteSet TakeForSuccess()
    {
        StopWatching();

        lock (_lock)
        {
            return _announced ? LiveWriteSet.Empty : _Writes(matchRows: true);
        }
    }

    /// <summary>
    /// Returns the tables that may be stored although the save threw or was cancelled: those of a save whose
    /// command ran outside a transaction of its own, so nothing rolled it back with the failure. A save that threw
    /// does not say which of its rows were stored, so no row is described.
    /// </summary>
    public LiveWriteSet TakeForFailure()
    {
        StopWatching();

        lock (_lock)
        {
            return _announced || _transactionStarted || !_commandSucceeded
                ? LiveWriteSet.Empty
                : _Writes(matchRows: false);
        }
    }

    private LiveWriteSet _Writes(bool matchRows)
    {
        if (_rows is null)
        {
            return new LiveWriteSet([.. _keys], []);
        }

        if (matchRows)
        {
            try
            {
                List<LiveRowChange> rows = new(_rows.Count);

                foreach (var row in _rows)
                {
                    if (!_keys.Contains(row.Key))
                    {
                        rows.Add(row.Resolve());
                    }
                }

                return new LiveWriteSet([.. _keys], rows);
            }
#pragma warning disable CA1031 // The fix would make it worse: tracking must not fail the application's save, which has already run.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                failed(exception);
            }
        }

        return new LiveWriteSet([.. _keys.Union(_rowKeys!, StringComparer.Ordinal)], []);
    }

    private void _OnTracked(object? sender, EntityTrackedEventArgs tracked) => _ChangedLate(tracked.Entry);

    private void _OnStateChanged(object? sender, EntityStateChangedEventArgs changed) => _ChangedLate(changed.Entry);

    // Runs inside the application's own change-tracker call, so it must not throw there.
    private void _ChangedLate(EntityEntry entry)
    {
        lock (_lock)
        {
            if (_watched is null || _keysOf is null || _dependentKeysOf is null)
            {
                return;
            }

            try
            {
                _keys.UnionWith(_keysOf(entry.Metadata));

                // The database removes or clears the rows that refer to a deleted row without a command of their
                // own. The delete's command names their tables, but a table this save holds rows of is not taken
                // from a command, so a late delete must name them here as a delete read at the start does.
                if (entry.State is EntityState.Deleted)
                {
                    _keys.UnionWith(_dependentKeysOf(entry.Metadata));
                }
            }
#pragma warning disable CA1031 // The fix would make it worse: tracking must not fail the application's save.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                // Which table changed is not known, so no row of this save can be trusted.
                _keys.UnionWith(_rowKeys!);
                failed(exception);
            }
        }
    }
}
