// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Tenancy;

[CollectionDefinition]
public sealed class TenantReadGuardCollection
    : ICollectionFixture<PostgreSqlReadGuardTenantFixture>,
        ICollectionFixture<SqlServerReadGuardTenantFixture>;

public sealed class PostgreSqlReadGuardTenantFixture() : ReadGuardTenantFixture(TenantDatabaseProvider.PostgreSql);

public sealed class SqlServerReadGuardTenantFixture() : ReadGuardTenantFixture(TenantDatabaseProvider.SqlServer);

[Collection<TenantReadGuardCollection>]
public sealed class PostgreSqlTenantReadGuardTests(PostgreSqlReadGuardTenantFixture fixture)
    : TenantReadGuardConformanceTests<PostgreSqlReadGuardTenantFixture>(fixture);

[Collection<TenantReadGuardCollection>]
public sealed class SqlServerTenantReadGuardTests(SqlServerReadGuardTenantFixture fixture)
    : TenantReadGuardConformanceTests<SqlServerReadGuardTenantFixture>(fixture);
