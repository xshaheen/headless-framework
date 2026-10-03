// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the EF-only collation and identifier-update scenarios against SQL Server.</summary>
[Collection<SqlServerTenantCatalogFixture>]
public sealed class SqlServerTenantCatalogEfSpecificTests(SqlServerTenantCatalogFixture fixture)
    : TenantCatalogEfSpecificTests<SqlServerTenantCatalogFixture>(fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public override Task should_resolve_tenants_through_a_headless_catalog_context(bool pooled)
    {
        return base.should_resolve_tenants_through_a_headless_catalog_context(pooled);
    }
}
