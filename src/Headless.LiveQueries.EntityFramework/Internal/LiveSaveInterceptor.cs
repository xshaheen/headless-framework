// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Headless.LiveQueries.Internal;

/// <summary>
/// Reads the tables a save is about to write from the change tracker, and tells the tracker how the save ended.
/// </summary>
internal sealed class LiveSaveInterceptor(LiveTableCatalog catalog, LiveWriteTracker tracker) : SaveChangesInterceptor
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

        try
        {
            // The change tracker names every table of an entity, including the ones a save of a table-per-type or
            // split entity does not touch this time but a query over the entity may read alone.
            foreach (var entry in context.ChangeTracker.Entries())
            {
                if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                {
                    keys.UnionWith(catalog.KeysOf(context, entry.Metadata));
                }
            }
        }
#pragma warning disable CA1031 // The fix would make it worse: tracking must not fail the application's save.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            // The save goes on; the tables are then taken from the text of the commands it sends.
            tracker.Failed(exception);
        }

        if (keys.Count > 0 && LiveQueryRecorder.Current is not null)
        {
            throw LiveReadOnlyRule.Violated("SaveChanges", keys);
        }

        try
        {
            tracker.BeginSave(context).Add(keys);
        }
#pragma warning disable CA1031 // The fix would make it worse: tracking must not fail the application's save.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            tracker.Failed(exception);
        }
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
