// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.BuildingBlocks.Abstractions;
using Framework.Core;
using Framework.Permissions.Entities;
using Framework.Permissions.Models;
using Framework.Primitives;

namespace Framework.Permissions.Definitions;

public interface IPermissionDefinitionSerializer
{
    (IReadOnlyCollection<PermissionGroupDefinitionRecord>, IReadOnlyCollection<PermissionDefinitionRecord>) Serialize(
        IEnumerable<PermissionGroupDefinition> groups
    );

    PermissionGroupDefinitionRecord Serialize(PermissionGroupDefinition group);

    PermissionDefinitionRecord Serialize(PermissionDefinition permission, PermissionGroupDefinition group);
}

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

            foreach (var permission in permissionGroup.GetFlatPermissions())
            {
                permissionRecords.Add(Serialize(permission, permissionGroup));
            }
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

            foreach (var property in group.Properties)
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

            foreach (var property in permission.Properties)
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
