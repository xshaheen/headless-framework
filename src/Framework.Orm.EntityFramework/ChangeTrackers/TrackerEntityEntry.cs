// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Framework.Orm.EntityFramework.ChangeTrackers;

public sealed class TrackerEntityEntry(string id, EntityEntry entry)
{
    public string Id { get; } = id;

    public EntityEntry Entry { get; } = entry;

    public List<TrackerNavigationEntry> Navigations { get; } =
        [.. entry.Navigations.Select(x => new TrackerNavigationEntry(x))];

    public bool IsModified
    {
        get { return field || Entry.State is EntityState.Modified || Navigations.Exists(n => n.IsModified); }
        set;
    }

    public void UpdateNavigationEntries()
    {
        foreach (var navigation in Navigations)
        {
            if (
                IsModified
                || Entry.State is EntityState.Modified
                || navigation.IsModified
                || navigation.Entry.IsModified
            )
            {
                continue;
            }

            navigation.UpdateOriginalValueList();
        }
    }
}
