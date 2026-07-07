// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Entities;
using Headless.Settings.Models;

namespace Tests.TestSetup;

public static class TestData
{
    public static readonly Faker Faker = new();

    /// <summary>Builds <paramref name="count"/> randomly-populated setting definitions through the definition-context factory.</summary>
    public static List<SettingDefinition> CreateDefinitions(int count)
    {
        var context = new SettingDefinitionContext(new Dictionary<string, SettingDefinition>(StringComparer.Ordinal));

        for (var i = 0; i < count; i++)
        {
            context.AddGenerated();
        }

        return [.. context.GetAll()];
    }

    /// <summary>Mints a randomly-populated setting definition into <paramref name="context"/> and returns it.</summary>
    public static SettingDefinition AddGenerated(this ISettingDefinitionContext context)
    {
        return context.Add(
            name: Faker.Random.Guid().ToString("N"),
            defaultValue: Faker.Random.String2(1, SettingDefinitionRecordConstants.DefaultValueMaxLength),
            displayName: Faker.Random.String2(1, SettingDefinitionRecordConstants.DisplayNameMaxLength),
            description: Faker.Random.String2(1, SettingDefinitionRecordConstants.DescriptionMaxLength),
            isVisibleToClients: Faker.Random.Bool(),
            isInherited: Faker.Random.Bool(),
            isEncrypted: Faker.Random.Bool()
        );
    }

    /// <summary>
    /// Re-mints a structurally-equal definition into <paramref name="context"/> through the factory.
    /// Test shim standing in for the removed pre-built <c>Add(SettingDefinition)</c> overload so that
    /// providers can register definitions produced ahead of time by <see cref="CreateDefinitions"/>.
    /// </summary>
    public static SettingDefinition Add(this ISettingDefinitionContext context, SettingDefinition definition)
    {
        var added = context.Add(
            definition.Name,
            definition.DefaultValue,
            definition.DisplayName,
            definition.Description,
            definition.IsVisibleToClients,
            definition.IsInherited,
            definition.IsEncrypted
        );

        added.Providers.AddRange(definition.Providers);

        foreach (var property in definition.ExtraProperties)
        {
            added[property.Key] = property.Value;
        }

        return added;
    }
}
