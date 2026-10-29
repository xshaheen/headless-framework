// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<SqlServerSettingsFixture>]
public sealed class SqlServerSettingsUnitOfWorkConformanceTests(SqlServerSettingsFixture fixture)
    : SettingsUnitOfWorkConformanceTests<SqlServerSettingsFixture>(fixture);
