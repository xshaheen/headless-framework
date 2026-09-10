// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the shared store-conformance suite (KTD10) against <see cref="InMemoryTenantCatalogStoreFixture"/>.</summary>
public sealed class InMemoryTenantStoreConformanceTests(InMemoryTenantCatalogStoreFixture fixture)
    : TenantStoreConformanceTests<InMemoryTenantCatalogStoreFixture>(fixture),
        IClassFixture<InMemoryTenantCatalogStoreFixture>;
