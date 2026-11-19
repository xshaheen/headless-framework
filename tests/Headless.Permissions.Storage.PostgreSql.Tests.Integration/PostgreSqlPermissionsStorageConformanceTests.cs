// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

[Collection<PostgreSqlPermissionsFixture>]
public sealed class PostgreSqlPermissionsStorageConformanceTests(PostgreSqlPermissionsFixture fixture)
    : PermissionsStorageConformanceTests<PostgreSqlPermissionsFixture>(fixture);
