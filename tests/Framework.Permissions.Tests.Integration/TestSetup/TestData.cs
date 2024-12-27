// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Permissions.Entities;
using Framework.Permissions.Models;

namespace Tests.TestSetup;

public static class TestData
{
    public static readonly Faker Faker = new();

    public static PermissionGroupDefinition AddGeneratedPermissionGroup(this IPermissionDefinitionContext context)
    {
        return context.AddGroup(
            name: Faker.Random.String2(1, PermissionGroupDefinitionRecordConstants.NameMaxLength),
            displayName: Faker.Random.String2(1, PermissionGroupDefinitionRecordConstants.DisplayNameMaxLength)
        );
    }

    public static PermissionDefinition AddGeneratedPermissionDefinition(this PermissionGroupDefinition group)
    {
        return group.AddChild(
            name: Faker.Random.String2(1, PermissionDefinitionRecordConstants.NameMaxLength),
            displayName: Faker.Random.String2(1, PermissionDefinitionRecordConstants.DisplayNameMaxLength),
            isEnabled: Faker.Random.Bool()
        );
    }
}
