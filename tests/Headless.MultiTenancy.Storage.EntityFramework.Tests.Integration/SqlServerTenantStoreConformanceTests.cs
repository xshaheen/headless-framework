// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the shared store-conformance suite (KTD10) against the EF store on SQL Server.</summary>
[Collection<SqlServerTenantCatalogFixture>]
public sealed class SqlServerTenantStoreConformanceTests(SqlServerTenantCatalogFixture fixture)
    : TenantStoreConformanceTests<SqlServerTenantCatalogFixture>(fixture);
