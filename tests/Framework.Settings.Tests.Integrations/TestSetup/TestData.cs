// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Settings.Entities;
using Framework.Settings.Models;

namespace Tests.TestSetup;

public static class TestData
{
    public static Faker<SettingDefinition> CreateSettingDefinitionFaker()
    {
        return new Faker<SettingDefinition>().CustomInstantiator(faker => new SettingDefinition(
            name: faker.Random.String2(1, SettingDefinitionRecordConstants.NameMaxLength),
            defaultValue: faker.Random.String2(1, SettingDefinitionRecordConstants.DefaultValueMaxLength),
            displayName: faker.Random.String2(1, SettingDefinitionRecordConstants.DisplayNameMaxLength),
            description: faker.Random.String2(1, SettingDefinitionRecordConstants.DescriptionMaxLength),
            isVisibleToClients: faker.Random.Bool(),
            isInherited: faker.Random.Bool(),
            isEncrypted: faker.Random.Bool()
        ));
    }
}
