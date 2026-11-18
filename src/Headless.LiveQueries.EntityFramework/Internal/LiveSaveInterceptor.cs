// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Headless.LiveQueries.Internal;

/// <summary>
/// Reads the tables a save is about to write from the change tracker, and tells the tracker how the save ended.
/// With row matching on, it keeps the rows themselves for the tables a live query reads through row filters.
/// </summary>
internal sealed class LiveSaveInterceptor(LiveTableCatalog catalog, LiveWriteTracker tracker, bool matchRows)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        _Saving(eventData.Context);

        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        _Saving(eventData.Context);

        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        _Saved(eventData.Context);

        return result;
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default
    )
    {
        _Saved(eventData.Context);

        return ValueTask.FromResult(result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        _Failed(eventData.Context);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        _Failed(eventData.Context);

        return Task.CompletedTask;
    }

    public override void SaveChangesCanceled(DbContextEventData eventData)
    {
        _Failed(eventData.Context);
    }

    public override Task SaveChangesCanceledAsync(
        DbContextEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        _Failed(eventData.Context);

        return Task.CompletedTask;
    }

    private void _Saving(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        List<LivePendingRow>? rows = null;

        try
        {
            // A live query is refused below and must be told every table, so it keeps no rows.
            var narrowed = matchRows && LiveQueryRecorder.Current is null ? new Dictionary<string, bool>() : null;

            // The change tracker names every table of an entity, including the ones a save of a table-per-type or
            // split entity does not touch this time but a query over the entity may read alone.
            foreach (var entry in context.ChangeTracker.Entries())
            {
                if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                {
                    continue;
                }

                if (narrowed is not null && _TryCaptureRow(entry, narrowed) is { } row)
                {
                    (rows ??= []).Add(row);

                    // A bulk save is announced as its tables; from here on it keeps no row.
                    if (rows.Count > LivePendingWrites.MaxRows)
                    {
                        keys.UnionWith(rows.Select(kept => kept.Key));
                        rows = null;
                        narrowed = null;
                    }
                }
                else
                {
                    keys.UnionWith(catalog.KeysOf(context, entry.Metadata));
                }

                // The database removes or clears the rows that refer to a deleted row without a command of their
                // own, and the context may never have loaded them. No row of theirs is known here, so their tables
                // are announced whole, whether or not the deleted row itself was kept.
                if (entry.State is EntityState.Deleted)
                {
                    keys.UnionWith(catalog.DependentKeysOf(context, entry.Metadata));
                }
            }
        }
#pragma warning disable CA1031 // The fix would make it worse: tracking must not fail the application's save.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            // The save goes on; the tables are then taken from the text of the commands it sends.
            rows = null;
            tracker.Failed(exception);
        }

        if (keys.Count > 0 && LiveQueryRecorder.Current is not null)
        {
            throw LiveReadOnlyRule.Violated("SaveChanges", keys);
        }

        try
        {
            var capture = tracker.BeginSave(context);
            capture.Add(keys);

            if (rows is not null)
            {
                capture.AddRows(
                    rows,
                    context.ChangeTracker,
                    entityType => catalog.KeysOf(context, entityType),
                    entityType => catalog.DependentKeysOf(context, entityType)
                );
            }
        }
#pragma warning disable CA1031 // The fix would make it worse: tracking must not fail the application's save.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            tracker.Failed(exception);
        }
    }

    // A row is kept only when it can spare a re-run: its entity type's rows can be matched, and a live query reads
    // the table through row filters right now. Every other write is announced as its tables, which costs nothing
    // here and re-runs every live query that read them.
    private LivePendingRow? _TryCaptureRow(EntityEntry entry, Dictionary<string, bool> narrowed)
    {
        var layout = LiveRowLayout.For(entry.Metadata);

        if (layout.Key is not { } key || !layout.IsMatchable)
        {
            return null;
        }

        if (!narrowed.TryGetValue(key, out var read))
        {
            narrowed[key] = read = tracker.HasNarrowedReader(key);
        }

        return read ? new LivePendingRow(entry, layout) : null;
    }

    private void _Saved(DbContext? context)
    {
        if (context is not null)
        {
            tracker.SaveSucceeded(context);
        }
    }

    private void _Failed(DbContext? context)
    {
        if (context is not null)
        {
            tracker.SaveFailed(context);
        }
    }
}
