// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Features;

namespace Tests.TestSetup;

public static class TestData
{
    public static readonly Faker Faker = new();

    // Definition names are unique keys: a one-letter random name collides with a sibling often enough to throw a
    // duplicate-name error, and a short one can match the 5-10 character name the "not defined" tests look up. Sixteen
    // characters or more makes both practically impossible.
    private static string _UniqueName(int maxLength) => Faker.Random.String2(16, maxLength);

    public static FeatureGroupDefinition AddGeneratedFeatureGroup(this IFeatureDefinitionContext context)
    {
        return context.AddGroup(
            name: _UniqueName(FeatureGroupDefinitionRecordConstants.NameMaxLength),
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
            new()
            {
                Name = source.Name,
                DefaultValue = source.DefaultValue,
                DisplayName = source.DisplayName,
                Description = source.Description,
                IsVisibleToClients = source.IsVisibleToClients,
                IsAvailableToHost = source.IsAvailableToHost,
            }
        );

        foreach (var child in source.Children)
        {
            _ReplayFeature(feature, child);
        }
    }

    public static FeatureDefinition AddGeneratedFeatureDefinition(this FeatureGroupDefinition group)
    {
        return group.AddChild(
            new()
            {
                Name = _UniqueName(FeatureDefinitionRecordConstants.NameMaxLength),
                DefaultValue = Faker.Random.String2(1, FeatureDefinitionRecordConstants.DefaultValueMaxLength),
                DisplayName = Faker.Random.String2(1, FeatureDefinitionRecordConstants.DisplayNameMaxLength),
                Description = Faker.Random.String2(1, FeatureDefinitionRecordConstants.DescriptionMaxLength),
                IsVisibleToClients = Faker.Random.Bool(),
                IsAvailableToHost = Faker.Random.Bool(),
            }
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
