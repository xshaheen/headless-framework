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
            new()
            {
                Name = Faker.Random.Guid().ToString("N"),
                DefaultValue = Faker.Random.String2(1, SettingDefinitionRecordConstants.DefaultValueMaxLength),
                DisplayName = Faker.Random.String2(1, SettingDefinitionRecordConstants.DisplayNameMaxLength),
                Description = Faker.Random.String2(1, SettingDefinitionRecordConstants.DescriptionMaxLength),
                IsVisibleToClients = Faker.Random.Bool(),
                IsInherited = Faker.Random.Bool(),
                IsEncrypted = Faker.Random.Bool(),
            }
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
            new()
            {
                Name = definition.Name,
                DefaultValue = definition.DefaultValue,
                DisplayName = definition.DisplayName,
                Description = definition.Description,
                IsVisibleToClients = definition.IsVisibleToClients,
                IsInherited = definition.IsInherited,
                IsEncrypted = definition.IsEncrypted,
            }
        );

        added.Providers.AddRange(definition.Providers);

        foreach (var property in definition.ExtraProperties)
        {
            added[property.Key] = property.Value;
        }

        return added;
    }
}
