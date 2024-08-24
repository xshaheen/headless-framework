using Framework.BuildingBlocks.Domains;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Framework.Orm.EntityFramework.ChangeTrackers;

/// <summary>
/// Refactor this class after EF Core supports this case.
/// <a href="https://github.com/dotnet/efcore/issues/24076#issuecomment-1996623874"></a>
/// </summary>
public sealed class EntityFrameworkNavigationHelper
{
    private Dictionary<string, HelperEntityEntry> HelperEntityEntries { get; } = [];

    public void ChangeTrackerTracked(object? _, EntityTrackedEventArgs e)
    {
        _EntityEntryTrackedOrStateChanged(e.Entry);
        _DetectChanges(e.Entry);
    }

    public void ChangeTrackerStateChanged(object? _, EntityStateChangedEventArgs e)
    {
        _EntityEntryTrackedOrStateChanged(e.Entry);
        _DetectChanges(e.Entry);
    }

    public List<EntityEntry> GetChangedEntityEntries()
    {
        return HelperEntityEntries.Where(x => x.Value.IsModified).Select(x => x.Value.EntityEntry).ToList();
    }

    public bool IsEntityEntryModified(EntityEntry entityEntry)
    {
        if (entityEntry.State is EntityState.Modified)
        {
            return true;
        }

        var entryId = _GetEntityEntryIdentity(entityEntry);

        if (entryId is null)
        {
            return false;
        }

        return HelperEntityEntries.TryGetValue(entryId, out var helperEntityEntry) && helperEntityEntry.IsModified;
    }

    public bool IsNavigationEntryModified(EntityEntry entityEntry, int? navigationEntryIndex = null)
    {
        var entryId = _GetEntityEntryIdentity(entityEntry);

        if (entryId is null)
        {
            return false;
        }

        if (!HelperEntityEntries.TryGetValue(entryId, out var entry))
        {
            return false;
        }

        if (navigationEntryIndex is null)
        {
            return entry.NavigationEntries.Any(x => x.IsModified);
        }

        var navigationEntryProperty = entry.NavigationEntries.ElementAtOrDefault(navigationEntryIndex.Value);

        return navigationEntryProperty is { IsModified: true };
    }

    public void RemoveChangedEntityEntries()
    {
        HelperEntityEntries.RemoveAll(x => x.Value.IsModified);
    }

    public void Clear()
    {
        HelperEntityEntries.Clear();
    }

    #region Helpers

    private void _DetectChanges(EntityEntry entityEntry)
    {
        if (entityEntry.State is not EntityState.Added and not EntityState.Deleted and not EntityState.Modified)
        {
            return;
        }

        _RecursiveDetectChanges(entityEntry);
    }

    private void _EntityEntryTrackedOrStateChanged(EntityEntry entityEntry)
    {
        if (entityEntry.State is not EntityState.Unchanged)
        {
            return;
        }

        var entryId = _GetEntityEntryIdentity(entityEntry);
        if (entryId is null)
        {
            return;
        }

        if (HelperEntityEntries.ContainsKey(entryId))
        {
            return;
        }

        HelperEntityEntries.Add(entryId, new HelperEntityEntry(entryId, entityEntry));
    }

    private void _RecursiveDetectChanges(EntityEntry entityEntry)
    {
#pragma warning disable EF1001
        var stateManager = entityEntry.Context.GetDependencies().StateManager;
        var internalEntityEntityEntry = stateManager.Entries.FirstOrDefault(x => x.Entity == entityEntry.Entity);

        if (internalEntityEntityEntry is null)
        {
            return;
        }

        var foreignKeys = entityEntry.Metadata.GetForeignKeys().ToList();
        foreach (var foreignKey in foreignKeys)
        {
            var principal = stateManager.FindPrincipal(internalEntityEntityEntry, foreignKey);
            if (principal is null)
            {
                continue;
            }

            var entryId = _GetEntityEntryIdentity(principal.ToEntityEntry());
            if (entryId is null || !HelperEntityEntries.TryGetValue(entryId, out var helperEntityEntry))
            {
                continue;
            }

            if (!helperEntityEntry.IsModified)
            {
                helperEntityEntry.IsModified = true;
                _RecursiveDetectChanges(helperEntityEntry.EntityEntry);
            }

            var navigationEntry =
                helperEntityEntry.NavigationEntries.FirstOrDefault(x =>
                    x.NavigationEntry.Metadata is INavigation navigationMetadata
                    && navigationMetadata.ForeignKey == foreignKey
                )
                ?? helperEntityEntry.NavigationEntries.FirstOrDefault(x =>
                    x.NavigationEntry.Metadata is ISkipNavigation skipNavigationMetadata
                    && skipNavigationMetadata.ForeignKey == foreignKey
                );

            if (navigationEntry is not null)
            {
                navigationEntry.IsModified = true;
            }
        }

        var skipNavigations = entityEntry.Metadata.GetSkipNavigations().ToList();

        foreach (var skipNavigation in skipNavigations)
        {
            var joinEntityType = skipNavigation.JoinEntityType;
            var foreignKey = skipNavigation.ForeignKey;
            var inverseForeignKey = skipNavigation.Inverse.ForeignKey;
            foreach (var joinEntry in stateManager.Entries)
            {
                if (
                    joinEntry.EntityType != joinEntityType
                    || stateManager.FindPrincipal(joinEntry, foreignKey) != internalEntityEntityEntry
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
                if (entryId is null || !HelperEntityEntries.TryGetValue(entryId, out var helperEntityEntry))
                {
                    continue;
                }

                if (!helperEntityEntry.IsModified)
                {
                    helperEntityEntry.IsModified = true;
                    _RecursiveDetectChanges(helperEntityEntry.EntityEntry);
                }

                var navigationEntry =
                    helperEntityEntry.NavigationEntries.FirstOrDefault(x =>
                        x.NavigationEntry.Metadata is INavigation navigationMetadata
                        && navigationMetadata.ForeignKey == inverseForeignKey
                    )
                    ?? helperEntityEntry.NavigationEntries.FirstOrDefault(x =>
                        x.NavigationEntry.Metadata is ISkipNavigation skipNavigationMetadata
                        && skipNavigationMetadata.ForeignKey == inverseForeignKey
                    );

                if (navigationEntry is not null)
                {
                    navigationEntry.IsModified = true;
                }
            }
        }
#pragma warning restore EF1001
    }

    private static string? _GetEntityEntryIdentity(EntityEntry entityEntry)
    {
        if (entityEntry.Entity is IEntity entryEntity && entryEntity.GetKeys().Count == 1)
        {
            return $"{entityEntry.Metadata.ClrType.FullName}:{entryEntity.GetKeys()[0]}";
        }

        return null;
    }

    #endregion
}
