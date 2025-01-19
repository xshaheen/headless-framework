// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Features.Entities;
using Framework.Features.Models;

namespace Tests.TestSetup;

public static class TestData
{
    public static readonly Faker Faker = new();

    public static FeatureGroupDefinition AddGeneratedFeatureGroup(this IFeatureDefinitionContext context)
    {
        return context.AddGroup(
            name: Faker.Random.String2(1, FeatureGroupDefinitionRecordConstants.NameMaxLength),
            displayName: Faker.Random.String2(1, FeatureGroupDefinitionRecordConstants.DisplayNameMaxLength)
        );
    }

    public static FeatureDefinition AddGeneratedFeatureDefinition(this FeatureGroupDefinition group)
    {
        return group.AddChild(
            name: Faker.Random.String2(1, FeatureDefinitionRecordConstants.NameMaxLength),
            defaultValue: Faker.Random.String2(1, FeatureDefinitionRecordConstants.DefaultValueMaxLength),
            displayName: Faker.Random.String2(1, FeatureDefinitionRecordConstants.DisplayNameMaxLength),
            description: Faker.Random.String2(1, FeatureDefinitionRecordConstants.DescriptionMaxLength),
            isVisibleToClients: Faker.Random.Bool(),
            isAvailableToHost: Faker.Random.Bool()
        );
    }

    public static FeatureGroupDefinition CreateGroupDefinition(int children = 3)
    {
        var context = new FeatureDefinitionContext();

        var group = context.AddGeneratedFeatureGroup();

        for (var i = 0; i < children; i++)
        {
            group.AddGeneratedFeatureDefinition();
        }

        return group;
    }
}
