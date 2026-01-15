// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Framework.Orm.EntityFramework.ChangeTrackers;

/// <summary>
/// Tracks modified navigations in Entity Framework Core.
/// because EF Core does not track navigation properties by default.
/// <a href="https://github.com/dotnet/efcore/issues/24076#issuecomment-1996623874">EF Core Issue #24076</a>
/// Note: This class only track entities that implement <see cref="IEntity"/> interface.
/// </summary>
public sealed class HeadlessEntityFrameworkNavigationModifiedTracker
{
    public void ChangeTrackerTracked(object? sender, EntityTrackedEventArgs e)
    {
        _EntityEntryTrackedOrStateChanged(e.Entry);
        _DetectChanges(e.Entry);
    }

    public void ChangeTrackerStateChanged(object? sender, EntityStateChangedEventArgs e)
    {
        _EntityEntryTrackedOrStateChanged(e.Entry);
        _DetectChanges(e.Entry);
    }

    private readonly Dictionary<string, HeadlessEntityEntry> _trackers = new(StringComparer.Ordinal);

    public List<EntityEntry> GetModifiedEntityEntries()
    {
        return _trackers.Where(x => x.Value.IsModified).Select(x => x.Value.Entry).ToList();
    }

    public HeadlessNavigationEntry? GetNavigationEntry(EntityEntry entry, int navigationEntryIndex)
    {
        var entryId = _GetEntityEntryIdentity(entry);

        if (entryId is null)
        {
            return null;
        }

        if (!_trackers.TryGetValue(entryId, out var tracker))
        {
            return null;
        }

        return tracker.Navigations.ElementAtOrDefault(navigationEntryIndex);
    }

    public bool IsEntityEntryModified(EntityEntry entry)
    {
        if (entry.State is EntityState.Modified)
        {
            return true;
        }

        var entryId = _GetEntityEntryIdentity(entry);

        if (entryId is null)
        {
            return false;
        }

        return _trackers.TryGetValue(entryId, out var tracker) && tracker.IsModified;
    }

    public bool IsNavigationEntryModified(EntityEntry entry, int? navigationEntryIndex = null)
    {
        var entryId = _GetEntityEntryIdentity(entry);

        if (entryId is null)
        {
            return false;
        }

        if (!_trackers.TryGetValue(entryId, out var tracker))
        {
            return false;
        }

        if (navigationEntryIndex is null)
        {
            return tracker.Navigations.Exists(x => x.IsModified);
        }

        var navigationEntryProperty = tracker.Navigations.ElementAtOrDefault(navigationEntryIndex.Value);

        return navigationEntryProperty?.IsModified is true;
    }

    public void RemoveModifiedEntityEntries() => _trackers.RemoveAll(x => x.Value.IsModified);

    public void Clear() => _trackers.Clear();

    #region Helper Methods

    private void _EntityEntryTrackedOrStateChanged(EntityEntry entry)
    {
        if (entry.State is not EntityState.Unchanged)
        {
            return;
        }

        var entryId = _GetEntityEntryIdentity(entry);

        if (entryId is null)
        {
            return;
        }

        if (!_trackers.ContainsKey(entryId))
        {
            _trackers.Add(entryId, new HeadlessEntityEntry(entryId, entry));
        }
    }

    private void _DetectChanges(EntityEntry entry, bool checkEntryState = true)
    {
        // INTERNAL EF CORE API USAGE
        // -----------------------------------------------------------------------------
        // Required: Access to StateManager for navigation change tracking. EF Core's
        //   public API does not expose navigation modification tracking (see issue #24076).
        //   We need StateManager to:
        //   - Get InternalEntityEntry via TryGetEntry() for relationship traversal
        //   - Find principal entities via FindPrincipal() for foreign key relationships
        //   - Iterate tracked entries via Entries for skip navigation detection
        // Tested with: EF Core 8.x, 9.x, 10.x
        // On EF Core upgrade: Verify the following APIs still exist:
        //   - DbContext.GetDependencies().StateManager
        //   - IStateManager.TryGetEntry(object, bool)
        //   - IStateManager.FindPrincipal(InternalEntityEntry, IForeignKey)
        //   - IStateManager.Entries
        //   - InternalEntityEntry.ToEntityEntry()
        // Alternative: None available in public API as of EF Core 10.0
        // Related: https://github.com/dotnet/efcore/issues/24076
        // -----------------------------------------------------------------------------
#pragma warning disable EF1001 // Internal EF Core API usage.
        var stateManager = entry.Context.GetDependencies().StateManager;

        var internalEntityEntry = stateManager.TryGetEntry(entry.Entity, throwOnNonUniqueness: false);

        if (internalEntityEntry is null)
        {
            return;
        }

        // References
        foreach (var foreignKey in entry.Metadata.GetForeignKeys())
        {
            var principal = stateManager.FindPrincipal(internalEntityEntry, foreignKey);

            if (principal is null)
            {
                continue;
            }

            var entryId = _GetEntityEntryIdentity(principal.ToEntityEntry());

            if (entryId is null || !_trackers.TryGetValue(entryId, out var tracker))
            {
                continue;
            }

            tracker.UpdateNavigationEntries();

            if (!tracker.IsModified && (!checkEntryState || _IsEntityEntryChanged(entry)))
            {
                tracker.IsModified = true;
                _DetectChanges(tracker.Entry, checkEntryState: false);
            }

            var navigationEntry =
                tracker.Navigations.FirstOrDefault(x =>
                    x.Entry.Metadata is INavigation navigationMetadata && navigationMetadata.ForeignKey == foreignKey
                )
                ?? tracker.Navigations.FirstOrDefault(x =>
                    x.Entry.Metadata is ISkipNavigation skipNavigationMetadata
                    && skipNavigationMetadata.ForeignKey == foreignKey
                );

            if (navigationEntry is not null && _IsEntityEntryChanged(entry))
            {
                navigationEntry.IsModified = true;
            }
        }

        // Navigations
        foreach (var skipNavigation in entry.Metadata.GetSkipNavigations())
        {
            var joinEntityType = skipNavigation.JoinEntityType;
            var foreignKey = skipNavigation.ForeignKey;
            var inverseForeignKey = skipNavigation.Inverse.ForeignKey;

            foreach (var joinEntry in stateManager.Entries)
            {
                if (
                    joinEntry.EntityType != joinEntityType
                    || stateManager.FindPrincipal(joinEntry, foreignKey) != internalEntityEntry
                )
                {
                    continue;
                }

                var principal = stateManager.FindPrincipal(joinEntry, inverseForeignKey);

                if (principal is null)
                {
                    continue;
                }

                var entryId = _GetEntityEntryIdentity(principal.ToEntityEntry());

                if (entryId is null || !_trackers.TryGetValue(entryId, out var tracker))
                {
                    continue;
                }

                tracker.UpdateNavigationEntries();

                if (!tracker.IsModified && (!checkEntryState || _IsEntityEntryChanged(entry)))
                {
                    tracker.IsModified = true;
                    _DetectChanges(tracker.Entry, checkEntryState: false);
                }

                var navigationEntry =
                    tracker.Navigations.FirstOrDefault(x =>
                        x.Entry.Metadata is INavigation navigationMetadata
                        && navigationMetadata.ForeignKey == inverseForeignKey
                    )
                    ?? tracker.Navigations.FirstOrDefault(x =>
                        x.Entry.Metadata is ISkipNavigation skipNavigationMetadata
                        && skipNavigationMetadata.ForeignKey == inverseForeignKey
                    );

                if (navigationEntry is not null && (!checkEntryState || _IsEntityEntryChanged(entry)))
                {
                    navigationEntry.IsModified = true;
                }
            }
        }
#pragma warning restore EF1001 // Internal EF Core API usage.
    }

    private static string? _GetEntityEntryIdentity(EntityEntry entry)
    {
        if (entry.Entity is IEntity entity)
        {
            var keys = entity.GetKeys();

            if (keys.Count == 0)
            {
                return null;
            }

            return $"{entry.Metadata.ClrType.FullName}:{keys.JoinAsString("|")}";
        }

        return null;
    }

    private static bool _IsEntityEntryChanged(EntityEntry entry)
    {
        return entry.State is EntityState.Added or EntityState.Deleted or EntityState.Modified;
    }

    #endregion
}
