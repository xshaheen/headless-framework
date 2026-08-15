// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the shared store-conformance suite (KTD10) against the EF store on PostgreSQL.</summary>
[Collection<PostgreSqlTenantCatalogFixture>]
public sealed class PostgreSqlTenantStoreConformanceTests(PostgreSqlTenantCatalogFixture fixture)
    : TenantStoreConformanceTests<PostgreSqlTenantCatalogFixture>(fixture);
