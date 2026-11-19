// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<SqlServerPermissionsFixture>]
public sealed class SqlServerPermissionsUnitOfWorkConformanceTests(SqlServerPermissionsFixture fixture)
    : PermissionsUnitOfWorkConformanceTests<SqlServerPermissionsFixture>(fixture);
