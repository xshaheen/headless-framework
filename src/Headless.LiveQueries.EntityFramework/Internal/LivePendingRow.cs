// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Headless.LiveQueries.Internal;

/// <summary>One row a save is writing, kept until the save has run and the row's stored values are known.</summary>
internal sealed class LivePendingRow
{
    private readonly EntityEntry _entry;
    private readonly LiveRowLayout _layout;
    private readonly EntityState _state;
    private readonly LiveRow? _stored;

    /// <summary>Captures <paramref name="entry" /> before its save runs.</summary>
    /// <param name="entry">An added, modified or deleted entry.</param>
    /// <param name="layout">The layout of the entry's entity type, whose rows can be matched.</param>
    public LivePendingRow(EntityEntry entry, LiveRowLayout layout)
    {
        _entry = entry;
        _layout = layout;
        _state = entry.State;

        // The row an update or a delete reaches exists now. An inserted row has its key only once the save ran.
        _stored = _state is EntityState.Added ? null : layout.ReadStored(entry);
    }

    public string Key => _layout.Key!;

    /// <summary>Returns the change, reading an inserted row's values. Call it after the save's commands have run.</summary>
    public LiveRowChange Resolve()
    {
        var row = _stored ?? _layout.ReadInserted(_entry);

        return new LiveRowChange(
            Key,
            _state is EntityState.Added ? null : row,
            _state is EntityState.Deleted ? null : row
        );
    }
}
