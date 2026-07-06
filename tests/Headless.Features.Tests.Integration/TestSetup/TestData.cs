// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features.Entities;
using Headless.Features.Models;

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

    /// <summary>
    /// Replays a prebuilt <paramref name="source"/> group (and all of its features, recursively) into
    /// <paramref name="context"/> using only the public string-based API — the interface no longer exposes
    /// an instance-taking <c>AddGroup(FeatureGroupDefinition)</c> overload.
    /// </summary>
    public static FeatureGroupDefinition AddGroup(this IFeatureDefinitionContext context, FeatureGroupDefinition source)
    {
        var group = context.AddGroup(source.Name, source.DisplayName);

        foreach (var feature in source.Features)
        {
            _ReplayFeature(group, feature);
        }

        return group;
    }

    private static void _ReplayFeature(ICanAddChildFeature parent, FeatureDefinition source)
    {
        var feature = parent.AddChild(
            source.Name,
            source.DefaultValue,
            source.DisplayName,
            source.Description,
            source.IsVisibleToClients,
            source.IsAvailableToHost
        );

        foreach (var child in source.Children)
        {
            _ReplayFeature(feature, child);
        }
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
