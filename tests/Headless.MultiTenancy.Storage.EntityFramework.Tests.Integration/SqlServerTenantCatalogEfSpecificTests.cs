// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the EF-only collation and identifier-update scenarios against SQL Server.</summary>
[Collection<SqlServerTenantCatalogFixture>]
public sealed class SqlServerTenantCatalogEfSpecificTests(SqlServerTenantCatalogFixture fixture)
    : TenantCatalogEfSpecificTests<SqlServerTenantCatalogFixture>(fixture);
