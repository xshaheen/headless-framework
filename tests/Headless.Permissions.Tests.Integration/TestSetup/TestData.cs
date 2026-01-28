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
