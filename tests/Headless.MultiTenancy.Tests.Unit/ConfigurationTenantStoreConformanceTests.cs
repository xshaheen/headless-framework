// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the shared store-conformance suite (KTD10) against <see cref="ConfigurationTenantCatalogStoreFixture"/>.</summary>
public sealed class ConfigurationTenantStoreConformanceTests(ConfigurationTenantCatalogStoreFixture fixture)
    : TenantStoreConformanceTests<ConfigurationTenantCatalogStoreFixture>(fixture),
        IClassFixture<ConfigurationTenantCatalogStoreFixture>;
