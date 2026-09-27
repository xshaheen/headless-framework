// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<PostgreSqlSettingsFixture>]
public sealed class PostgreSqlSettingsStorageConformanceTests(PostgreSqlSettingsFixture fixture)
    : SettingsStorageConformanceTests<PostgreSqlSettingsFixture>(fixture);
