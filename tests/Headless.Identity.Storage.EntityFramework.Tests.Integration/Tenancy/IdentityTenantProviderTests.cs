// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Tenancy;

[CollectionDefinition(DisableParallelization = true)]
public sealed class IdentityTenantCollection
    : ICollectionFixture<PostgreSqlIdentityTenantFixture>,
        ICollectionFixture<SqlServerIdentityTenantFixture>;

public sealed class PostgreSqlIdentityTenantFixture()
    : TenantIdentityFixture<DefaultIdentityPolicy>(TenantDatabaseProvider.PostgreSql);

public sealed class SqlServerIdentityTenantFixture()
    : TenantIdentityFixture<DefaultIdentityPolicy>(TenantDatabaseProvider.SqlServer);

[Collection<IdentityTenantCollection>]
public sealed class PostgreSqlIdentityTenantConformanceTests(PostgreSqlIdentityTenantFixture fixture)
    : IdentityTenantConformanceTests<PostgreSqlIdentityTenantFixture>(fixture);

[Collection<IdentityTenantCollection>]
public sealed class SqlServerIdentityTenantConformanceTests(SqlServerIdentityTenantFixture fixture)
    : IdentityTenantConformanceTests<SqlServerIdentityTenantFixture>(fixture);
