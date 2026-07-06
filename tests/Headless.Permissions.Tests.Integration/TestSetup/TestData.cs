// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions.Entities;
using Headless.Permissions.Models;

namespace Tests.TestSetup;

public static class TestData
{
    public static readonly Faker Faker = new();

    public static PermissionGroupDefinition AddGeneratedGroup(this IPermissionDefinitionContext context)
    {
        return context.AddGroup(
            name: Faker.Random.String2(1, PermissionGroupDefinitionRecordConstants.NameMaxLength),
            displayName: Faker.Random.String2(1, PermissionGroupDefinitionRecordConstants.DisplayNameMaxLength)
        );
    }

    /// <summary>
    /// Replays a prebuilt <paramref name="source"/> group (and all of its permissions, recursively) into
    /// <paramref name="context"/> using only the public string-based API — the interface no longer exposes
    /// an instance-taking <c>AddGroup(PermissionGroupDefinition)</c> overload.
    /// </summary>
    public static PermissionGroupDefinition AddGroup(
        this IPermissionDefinitionContext context,
        PermissionGroupDefinition source
    )
    {
        var group = context.AddGroup(source.Name, source.DisplayName);

        foreach (var permission in source.Permissions)
        {
            _ReplayPermission(group, permission);
        }

        return group;
    }

    private static void _ReplayPermission(ICanAddChildPermission parent, PermissionDefinition source)
    {
        var permission = parent.AddChild(source.Name, source.DisplayName, source.IsEnabled);

        foreach (var child in source.Children)
        {
            _ReplayPermission(permission, child);
        }
    }

    public static PermissionDefinition AddGeneratedDefinition(this PermissionGroupDefinition group)
    {
        return group.AddChild(
            name: Faker.Random.String2(1, PermissionDefinitionRecordConstants.NameMaxLength),
            displayName: Faker.Random.String2(1, PermissionDefinitionRecordConstants.DisplayNameMaxLength),
            isEnabled: true
        );
    }

    public static PermissionGroupDefinition CreateGroupDefinition(int children = 3)
    {
        var context = new PermissionDefinitionContext();

        var group = context.AddGeneratedGroup();

        for (var i = 0; i < children; i++)
        {
            group.AddGeneratedDefinition();
        }

        return group;
    }
}
