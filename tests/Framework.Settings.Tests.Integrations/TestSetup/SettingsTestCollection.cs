// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Tests.TestSetup;

[CollectionDefinition(nameof(SettingsTestCollection))]
public class SettingsTestCollection : ICollectionFixture<SettingsTestFixture>;
