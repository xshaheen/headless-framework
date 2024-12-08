// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Audit.Core;
using Audit.EntityFramework;
using Framework.Domains;
using Framework.Orm.EntityFramework.AuditLogs.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Framework.Orm.EntityFramework.AuditLogs;

public static class AuditLogCreator
{
    private static readonly ConcurrentDictionary<string, List<TrackedEntity>> _EntityTrackedPropertiesMap = new(
        StringComparer.Ordinal
    );

    public static IEnumerable<AuditLog> CreateAudit(IAuditScope auditScope)
    {
        if (auditScope.Event is not AuditEventEntityFramework auditEvent)
        {
            yield break;
        }

        var eventEntries = auditEvent.EntityFrameworkEvent.Entries;

        foreach (var eventEntry in eventEntries)
        {
            if (eventEntry.Entity is not IAggregateRoot)
            {
                continue;
            }

            var entityEntry = eventEntry.GetEntry();

            if (
                entityEntry.State is not EntityState.Modified
                || entityEntry.Metadata.ClrType.GetCustomAttribute<AuditAttribute>() is null
            )
            {
                continue;
            }

            var parent = new ParentEntity(entityEntry, _GetPrincipalKey(entityEntry), eventEntry.Name);
            var changes = _GetChanges(eventEntry, eventEntries, parent, parent);

            if (changes.Count == 0)
            {
                continue;
            }

            yield return AuditLog.Create(parent.PrincipalKey.Value, eventEntry.Name, changes);
        }
    }

    private static List<AuditLogChange> _GetChanges(
        EventEntry entry,
        List<EventEntry> allEntries,
        in ParentEntity primitiveParent,
        in ParentEntity navigationParent
    )
    {
        var trackedProperties = _GetTrackedProperties(entry);

        var primitivesActualChangesCount = 0;
        var primitiveChanges = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var collectionsAuditLogChanges = new List<AuditLogChange>();

        foreach (var trackedProperty in trackedProperties)
        {
            switch (trackedProperty.PropertyKind)
            {
                case TrackedEntityKind.Primitive:
                {
                    var change = entry.Changes.Find(change =>
                        string.Equals(
                            change.ColumnName,
                            trackedProperty.StorageName,
                            StringComparison.OrdinalIgnoreCase
                        )
                    );

                    if (change is not null && _HasActualChanged(trackedProperty, change))
                    {
                        primitiveChanges[trackedProperty.PropertyName] = change;
                        ++primitivesActualChangesCount;

                        continue;
                    }

                    if (trackedProperty.KeepAlways)
                    {
                        primitiveChanges[trackedProperty.PropertyName] = entry.ColumnValues[
                            trackedProperty.StorageName
                        ];
                    }

                    continue;
                }
                case TrackedEntityKind.Collection:
                {
                    var childCollectionEventEntries = _FilterEventEntriesByParentChild(
                        allEntries,
                        navigationParent,
                        trackedProperty
                    );

                    foreach (var collectionEventEntry in childCollectionEventEntries)
                    {
                        var collectionEntry = collectionEventEntry.GetEntry();
                        var collectionPrincipalKey = _GetPrincipalKey(collectionEntry);

                        var newPrimitiveParent = navigationParent;
                        var newNavigationParent = new ParentEntity(
                            collectionEntry,
                            collectionPrincipalKey,
                            collectionEventEntry.Name
                        );

                        var changes = collectionEventEntry.Action switch
                        {
                            "Insert" or "Delete" => _GetValues(
                                collectionEventEntry,
                                allEntries,
                                newPrimitiveParent,
                                newNavigationParent
                            ),
                            "Update" => _GetChanges(
                                collectionEventEntry,
                                allEntries,
                                newPrimitiveParent,
                                newNavigationParent
                            ),
                            _ => throw new InvalidOperationException(
                                $"Unknown collection event action: {collectionEventEntry.Action}"
                            ),
                        };

                        collectionsAuditLogChanges.AddRange(changes);
                    }

                    continue;
                }
                case TrackedEntityKind.Reference:
                    throw new NotSupportedException("Reference properties are not supported yet.");
                default:
                    throw new InvalidOperationException(
                        $"Unknown tracked property kind: {trackedProperty.PropertyKind}"
                    );
            }
        }

        // TODO: do diff between the collection changes to avoid track re-insertion (removed then insert) as a change
        if (primitivesActualChangesCount == 0 || primitiveChanges.Count == 0)
        {
            // return collectionsAuditLogChanges.All(_ => true) ? new List<AuditLogChange>() : collectionsAuditLogChanges;
        }

        var principalKey = _GetPrincipalKey(entry.GetEntry());

        var auditChange = AuditLogChange.CreateInstance(
            entityKey: principalKey.Value,
            entityType: entry.Name,
            parentEntityKey: primitiveParent.PrincipalKey.Value,
            parentEntityType: primitiveParent.Name,
            action: entry.Action,
            change: primitiveChanges
        );

        collectionsAuditLogChanges.Add(auditChange);

        return collectionsAuditLogChanges;
    }

    private static List<AuditLogChange> _GetValues(
        EventEntry entry,
        List<EventEntry> allEntries,
        in ParentEntity primitiveParent,
        in ParentEntity navigationParent
    )
    {
        var trackedProperties = _GetTrackedProperties(entry);
        var auditLogChanges = new List<AuditLogChange>();
        var primitiveChanges = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var trackedProperty in trackedProperties)
        {
            switch (trackedProperty.PropertyKind)
            {
                case TrackedEntityKind.Primitive:
                {
                    primitiveChanges[trackedProperty.PropertyName] = entry.ColumnValues[trackedProperty.StorageName];

                    continue;
                }
                case TrackedEntityKind.Collection:
                {
                    var childCollectionEventEntries = _FilterEventEntriesByParentChild(
                        allEntries,
                        navigationParent,
                        trackedProperty
                    );

                    foreach (var collectionEventEntry in childCollectionEventEntries)
                    {
                        var collectionEntry = collectionEventEntry.GetEntry();
                        var collectionEntityKey = _GetPrincipalKey(collectionEntry);

                        var newPrimitiveParent = navigationParent;
                        var newNavigationParent = new ParentEntity(
                            collectionEntry,
                            collectionEntityKey,
                            collectionEventEntry.Name
                        );

                        var collectionAuditLogChanges = _GetValues(
                            collectionEventEntry,
                            allEntries,
                            newPrimitiveParent,
                            newNavigationParent
                        );

                        if (collectionAuditLogChanges.Count > 0)
                        {
                            auditLogChanges.AddRange(collectionAuditLogChanges);
                        }
                    }

                    continue;
                }
                case TrackedEntityKind.Reference:
                    throw new NotSupportedException("Reference properties are not supported yet.");
                default:
                    throw new InvalidOperationException(
                        $"Unknown tracked property kind: {trackedProperty.PropertyKind}"
                    );
            }
        }

        if (primitiveChanges.Count == 0)
        {
            return auditLogChanges;
        }

        var principalKey = _GetPrincipalKey(entry.GetEntry());

        var auditChange = AuditLogChange.CreateInstance(
            entityKey: principalKey.Value,
            entityType: entry.Name,
            parentEntityKey: primitiveParent.PrincipalKey.Value,
            parentEntityType: primitiveParent.Name,
            action: entry.Action,
            change: primitiveChanges
        );

        auditLogChanges.Add(auditChange);

        return auditLogChanges;
    }

    private static IEnumerable<EventEntry> _FilterEventEntriesByParentChild(
        List<EventEntry> eventEntries,
        ParentEntity parent,
        TrackedEntity child
    )
    {
        foreach (var eventEntry in eventEntries)
        {
            if (!string.Equals(eventEntry.Name, child.PropertyType.Name, StringComparison.Ordinal))
            {
                continue;
            }

            var childNavigation =
                parent.Entry.Metadata.FindNavigation(child.PropertyName)
                ?? throw new InvalidOperationException(
                    $"Navigation {child.PropertyName} not found on {parent.Entry.Metadata.Name}"
                );

            var childEntry = eventEntry.GetEntry();

            var foreignValues = childNavigation.ForeignKey.Properties.Select(p =>
                childEntry.Property(p.Name).CurrentValue
            );
            var principalValues = parent.PrincipalKey.Key.Properties.Select(p =>
                parent.Entry.Property(p.Name).CurrentValue
            );
            var isChildEntity = principalValues.SequenceEqual(foreignValues);

            if (isChildEntity)
            {
                yield return eventEntry;
            }
        }
    }

    private static bool _HasActualChanged(TrackedEntity trackedProperty, EventEntryChange change)
    {
        if (change.OriginalValue is null && change.NewValue is null)
        {
            return false;
        }

        if (
            (change.OriginalValue is not null && change.NewValue is null)
            || (change.OriginalValue is null && change.NewValue is not null)
        )
        {
            return true;
        }

        Debug.Assert(change.OriginalValue is not null, "change.OriginalValue is not null");
        Debug.Assert(change.NewValue is not null, "change.NewValue is not null");

        if (trackedProperty.PropertyType == typeof(decimal) || trackedProperty.PropertyType == typeof(decimal?))
        {
            return Math.Abs((decimal)change.OriginalValue - (decimal)change.NewValue) > 0.00001m;
        }

        if (trackedProperty.PropertyType == typeof(double) || trackedProperty.PropertyType == typeof(double?))
        {
            return Math.Abs((double)change.OriginalValue - (double)change.NewValue) > 0.00001d;
        }

        if (trackedProperty.PropertyType == typeof(float) || trackedProperty.PropertyType == typeof(float?))
        {
            return Math.Abs((float)change.OriginalValue - (float)change.NewValue) > 0.00001f;
        }

        return !string.Equals(
            change.OriginalValue.ToInvariantString(),
            change.NewValue.ToInvariantString(),
            StringComparison.Ordinal
        );
    }

    private static List<TrackedEntity> _GetTrackedProperties(EventEntry eventEntry)
    {
        if (_EntityTrackedPropertiesMap.TryGetValue(eventEntry.Table, out var trackedProperties))
        {
            return trackedProperties;
        }

        trackedProperties = getTrackedProperties(eventEntry).ToList();
        _ = _EntityTrackedPropertiesMap.TryAdd(eventEntry.Table, trackedProperties);

        return trackedProperties;

        static IEnumerable<TrackedEntity> getTrackedProperties(EventEntry eventEntry)
        {
            var entityEntry = eventEntry.GetEntry();

            var trackedPropertyInfos = entityEntry
                .Entity.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(propertyInfo => propertyInfo.GetCustomAttribute<TrackAttribute>() is not null);

            var storeObject = StoreObjectIdentifier.Table(eventEntry.Table, eventEntry.Schema);

            foreach (var propertyInfo in trackedPropertyInfos)
            {
                var primitive = entityEntry.Properties.FirstOrDefault(x =>
                    string.Equals(x.Metadata.Name, propertyInfo.Name, StringComparison.OrdinalIgnoreCase)
                );

                if (primitive is not null)
                {
                    var columnName =
                        primitive.Metadata.GetColumnName(storeObject)
                        ?? throw new InvalidOperationException(
                            $"The property `{propertyInfo.Name}` has no column name in the table `{eventEntry.Table}`. Primitive without column name is not supported."
                        );

                    var trackedEntity = new TrackedEntity
                    {
                        PropertyName = primitive.Metadata.Name,
                        StorageName = columnName,
                        PropertyType = primitive.Metadata.ClrType,
                        PropertyKind = TrackedEntityKind.Primitive,
                        KeepAlways = propertyInfo.GetCustomAttribute<TrackAttribute>()?.KeepAlways == true,
                    };

                    yield return trackedEntity;

                    continue;
                }

                var collection = entityEntry.Collections.FirstOrDefault(x =>
                    string.Equals(x.Metadata.Name, propertyInfo.Name, StringComparison.OrdinalIgnoreCase)
                );

                if (collection is not null)
                {
                    var collectionTableName =
                        collection.Metadata.TargetEntityType.GetTableName()
                        ?? throw new InvalidOperationException(
                            $"The navigation collection `{propertyInfo.Name}` in the entity `{entityEntry.Metadata.Name}` has no table. Collection without table is not supported."
                        );

                    var trackedEntity = new TrackedEntity
                    {
                        PropertyName = collection.Metadata.Name,
                        StorageName = collectionTableName,
                        PropertyType = collection.Metadata.ClrType.GetGenericArguments()[0],
                        PropertyKind = TrackedEntityKind.Collection,
                        KeepAlways = propertyInfo.GetCustomAttribute<TrackAttribute>()?.KeepAlways == true,
                    };

                    yield return trackedEntity;

                    continue;
                }

                var references =
                    entityEntry.References.FirstOrDefault(x =>
                        string.Equals(x.Metadata.Name, propertyInfo.Name, StringComparison.OrdinalIgnoreCase)
                    ) ?? throw new NotSupportedException($"Not supported property type {propertyInfo.Name}");

                var referenceTableName =
                    references.Metadata.TargetEntityType.GetTableName()
                    ?? throw new InvalidOperationException(
                        $"The navigation reference `{propertyInfo.Name}` in the entity `{entityEntry.Metadata.Name}` has no table. Reference without table is not supported."
                    );

                yield return new TrackedEntity
                {
                    PropertyName = references.Metadata.Name,
                    StorageName = referenceTableName,
                    PropertyType = references.Metadata.ClrType,
                    PropertyKind = TrackedEntityKind.Reference,
                    KeepAlways = propertyInfo.GetCustomAttribute<TrackAttribute>()?.KeepAlways == true,
                };
            }
        }
    }

    private static EntityKey _GetPrincipalKey(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();

        if (key is null || key.Properties.Count == 0)
        {
            throw new InvalidOperationException($"Entity {entry.Metadata.Name} must have a key");
        }

        var keyValue = string.Join(
            ", ",
            key.Properties.Select(property => entry.Property(property.Name).CurrentValue.ToInvariantString())
        );

        return new(key, keyValue);
    }

    #region Helper Types

    private sealed record TrackedEntity
    {
        /// <summary>Column name if <see cref="PropertyKind"/> is <see cref="TrackedEntityKind.Primitive"/>, otherwise table name.</summary>
        public string StorageName { get; init; } = default!;

        public string PropertyName { get; init; } = default!;

        public Type PropertyType { get; init; } = default!;

        public TrackedEntityKind PropertyKind { get; init; }

        public bool KeepAlways { get; init; }
    }

    private readonly record struct ParentEntity(EntityEntry Entry, EntityKey PrincipalKey, string Name);

    private readonly record struct EntityKey(IReadOnlyKey Key, string Value);

    private enum TrackedEntityKind
    {
        Primitive,
        Reference,
        Collection,
    }

    #endregion
}
