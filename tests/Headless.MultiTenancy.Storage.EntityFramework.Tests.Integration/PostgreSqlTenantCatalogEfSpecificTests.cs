// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the EF-only collation and identifier-update scenarios against PostgreSQL.</summary>
[Collection<PostgreSqlTenantCatalogFixture>]
public sealed class PostgreSqlTenantCatalogEfSpecificTests(PostgreSqlTenantCatalogFixture fixture)
    : TenantCatalogEfSpecificTests<PostgreSqlTenantCatalogFixture>(fixture);
