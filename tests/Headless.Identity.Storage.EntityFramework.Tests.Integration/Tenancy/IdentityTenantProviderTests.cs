// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Tenancy;

// One collection per provider: a collection fails every test in it when any of its fixtures fails to start, so a
// SQL Server container that cannot start must not take the PostgreSQL classes down with it.
[CollectionDefinition(DisableParallelization = true)]
public sealed class PostgreSqlIdentityTenantCollection : ICollectionFixture<PostgreSqlIdentityTenantFixture>;

[CollectionDefinition(DisableParallelization = true)]
public sealed class SqlServerIdentityTenantCollection : ICollectionFixture<SqlServerIdentityTenantFixture>;

public sealed class PostgreSqlIdentityTenantFixture()
    : TenantIdentityFixture<DefaultIdentityPolicy>(TenantDatabaseProvider.PostgreSql);

public sealed class SqlServerIdentityTenantFixture()
    : TenantIdentityFixture<DefaultIdentityPolicy>(TenantDatabaseProvider.SqlServer);

[Collection<PostgreSqlIdentityTenantCollection>]
public sealed class PostgreSqlIdentityTenantConformanceTests(PostgreSqlIdentityTenantFixture fixture)
    : IdentityTenantConformanceTests<PostgreSqlIdentityTenantFixture>(fixture);

[Collection<SqlServerIdentityTenantCollection>]
public sealed class SqlServerIdentityTenantConformanceTests(SqlServerIdentityTenantFixture fixture)
    : IdentityTenantConformanceTests<SqlServerIdentityTenantFixture>(fixture);
