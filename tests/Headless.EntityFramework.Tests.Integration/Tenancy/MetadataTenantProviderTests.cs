// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Tenancy;

[CollectionDefinition(DisableParallelization = true)]
public sealed class MetadataTenantCollection
    : ICollectionFixture<PostgreSqlMetadataTenantFixture>,
        ICollectionFixture<SqlServerMetadataTenantFixture>;

public sealed class PostgreSqlMetadataTenantFixture() : MetadataTenantFixture(TenantDatabaseProvider.PostgreSql);

public sealed class SqlServerMetadataTenantFixture() : MetadataTenantFixture(TenantDatabaseProvider.SqlServer);

[Collection<MetadataTenantCollection>]
public sealed class PostgreSqlMetadataTenantConformanceTests(PostgreSqlMetadataTenantFixture fixture)
    : MetadataTenantConformanceTests<PostgreSqlMetadataTenantFixture>(fixture);

[Collection<MetadataTenantCollection>]
public sealed class SqlServerMetadataTenantConformanceTests(SqlServerMetadataTenantFixture fixture)
    : MetadataTenantConformanceTests<SqlServerMetadataTenantFixture>(fixture);
