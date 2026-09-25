// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<SqlServerSettingsFixture>]
public sealed class SqlServerSettingsStorageConformanceTests(SqlServerSettingsFixture fixture)
    : SettingsStorageConformanceTests<SqlServerSettingsFixture>(fixture);
