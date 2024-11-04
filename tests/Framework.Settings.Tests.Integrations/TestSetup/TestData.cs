// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Settings.Entities;
using Framework.Settings.Models;

namespace Tests.TestSetup;

public static class TestData
{
    public static Faker<SettingDefinition> CreateSettingDefinitionFaker()
    {
        return new Faker<SettingDefinition>().CustomInstantiator(faker => new SettingDefinition(
            name: faker.Random.String(SettingDefinitionRecordConstants.NameMaxLength),
            defaultValue: faker.Random.String(SettingDefinitionRecordConstants.NameMaxLength),
            displayName: faker.Random.String(),
            description: faker.Random.String(),
            isVisibleToClients: faker.Random.Bool(),
            isInherited: faker.Random.Bool(),
            isEncrypted: faker.Random.Bool()
        ));
    }
}
