// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Tenancy;

// One collection per provider: a collection fails every test in it when any of its fixtures fails to start, so a
// SQL Server container that cannot start must not take the PostgreSQL classes down with it.
[CollectionDefinition]
public sealed class PostgreSqlTenantReadGuardCollection : ICollectionFixture<PostgreSqlReadGuardTenantFixture>;

[CollectionDefinition]
public sealed class SqlServerTenantReadGuardCollection : ICollectionFixture<SqlServerReadGuardTenantFixture>;

public sealed class PostgreSqlReadGuardTenantFixture() : ReadGuardTenantFixture(TenantDatabaseProvider.PostgreSql);

public sealed class SqlServerReadGuardTenantFixture() : ReadGuardTenantFixture(TenantDatabaseProvider.SqlServer);

[Collection<PostgreSqlTenantReadGuardCollection>]
public sealed class PostgreSqlTenantReadGuardTests(PostgreSqlReadGuardTenantFixture fixture)
    : TenantReadGuardConformanceTests<PostgreSqlReadGuardTenantFixture>(fixture);

[Collection<SqlServerTenantReadGuardCollection>]
public sealed class SqlServerTenantReadGuardTests(SqlServerReadGuardTenantFixture fixture)
    : TenantReadGuardConformanceTests<SqlServerReadGuardTenantFixture>(fixture);
