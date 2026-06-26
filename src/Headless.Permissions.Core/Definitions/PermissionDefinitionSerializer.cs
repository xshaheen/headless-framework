// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Core;
using Headless.Permissions.Entities;
using Headless.Permissions.Models;
using Headless.Primitives;

namespace Headless.Permissions.Definitions;

/// <summary>
/// Converts in-memory permission definitions into their database record equivalents so they can be
/// persisted by <see cref="Repositories.IPermissionDefinitionRecordRepository"/>.
/// </summary>
public interface IPermissionDefinitionSerializer
{
    /// <summary>
    /// Serializes a collection of permission groups and all their flattened child permissions into
    /// parallel record lists ready for batch persistence. Each record receives a new <see cref="Guid"/>
    /// id from <c>IGuidGenerator</c>.
    /// </summary>
    (IReadOnlyCollection<PermissionGroupDefinitionRecord>, IReadOnlyCollection<PermissionDefinitionRecord>) Serialize(
        IEnumerable<PermissionGroupDefinition> groups
    );

    /// <summary>Serializes a single permission group definition into a database record.</summary>
    PermissionGroupDefinitionRecord Serialize(PermissionGroupDefinition group);

    /// <summary>
    /// Serializes a single permission definition into a database record. The owning
    /// <paramref name="group"/> is required to populate <see cref="PermissionDefinitionRecord.GroupName"/>.
    /// The permission's <see cref="PermissionDefinition.Providers"/> list is stored as a comma-joined string.
    /// </summary>
    /// <param name="permission">The permission definition to serialize.</param>
    /// <param name="group">The group that owns this permission; used to set <c>GroupName</c> on the record.</param>
    PermissionDefinitionRecord Serialize(PermissionDefinition permission, PermissionGroupDefinition group);
}

/// <summary>Default implementation of <see cref="IPermissionDefinitionSerializer"/>.</summary>
public sealed class PermissionDefinitionSerializer(IGuidGenerator guidGenerator) : IPermissionDefinitionSerializer
{
    public (
        IReadOnlyCollection<PermissionGroupDefinitionRecord>,
        IReadOnlyCollection<PermissionDefinitionRecord>
    ) Serialize(IEnumerable<PermissionGroupDefinition> groups)
    {
        var permissionGroupRecords = new List<PermissionGroupDefinitionRecord>();
        var permissionRecords = new List<PermissionDefinitionRecord>();

        foreach (var permissionGroup in groups)
        {
            permissionGroupRecords.Add(Serialize(permissionGroup));
            permissionRecords.AddRange(
                permissionGroup.GetFlatPermissions().Select(permission => Serialize(permission, permissionGroup))
            );
        }

        return (permissionGroupRecords.ToArray(), permissionRecords.ToArray());
    }

    public PermissionGroupDefinitionRecord Serialize(PermissionGroupDefinition group)
    {
        using (CultureHelper.Use(CultureInfo.InvariantCulture))
        {
            var permissionGroupRecord = new PermissionGroupDefinitionRecord(
                guidGenerator.Create(),
                group.Name,
                group.DisplayName
            );

            foreach (var property in group.ExtraProperties)
            {
                permissionGroupRecord.SetProperty(property.Key, property.Value);
            }

            return permissionGroupRecord;
        }
    }

    public PermissionDefinitionRecord Serialize(PermissionDefinition permission, PermissionGroupDefinition group)
    {
        using (CultureHelper.Use(CultureInfo.InvariantCulture))
        {
            var permissionRecord = new PermissionDefinitionRecord(
                guidGenerator.Create(),
                group.Name,
                permission.Name,
                permission.Parent?.Name,
                permission.DisplayName,
                permission.IsEnabled,
                _SerializeProviders(permission.Providers)
            );

            foreach (var property in permission.ExtraProperties)
            {
                permissionRecord.SetProperty(property.Key, property.Value);
            }

            return permissionRecord;
        }
    }

    private static string? _SerializeProviders(List<string> providers)
    {
        return providers.Count != 0 ? providers.JoinAsString(",") : null;
    }
}
