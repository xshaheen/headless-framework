// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<SqlServerPermissionsFixture>]
public sealed class SqlServerPermissionsStorageConformanceTests(SqlServerPermissionsFixture fixture)
    : PermissionsStorageConformanceTests<SqlServerPermissionsFixture>(fixture);
