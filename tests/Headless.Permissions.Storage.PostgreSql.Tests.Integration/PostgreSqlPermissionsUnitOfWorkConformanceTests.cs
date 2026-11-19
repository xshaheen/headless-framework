// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<PostgreSqlPermissionsFixture>]
public sealed class PostgreSqlPermissionsUnitOfWorkConformanceTests(PostgreSqlPermissionsFixture fixture)
    : PermissionsUnitOfWorkConformanceTests<PostgreSqlPermissionsFixture>(fixture);
