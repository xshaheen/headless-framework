// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<PostgreSqlSettingsFixture>]
public sealed class PostgreSqlSettingsUnitOfWorkConformanceTests(PostgreSqlSettingsFixture fixture)
    : SettingsUnitOfWorkConformanceTests<PostgreSqlSettingsFixture>(fixture);
